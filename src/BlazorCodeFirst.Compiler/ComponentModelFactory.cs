using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BlazorCodeFirst.Compiler.Analysis;
using BlazorCodeFirst.Compiler.Diagnostics;
using BlazorCodeFirst.Compiler.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler;

/// <summary>
/// Turns a candidate class node into an emittable component model in two symbol-free stages:
/// <see cref="Analyze"/> (semantic, runs inside the syntax-provider transform) and
/// <see cref="Expand"/> (a pure value transform combined with the view part registry).
/// </summary>
internal static class ComponentModelFactory
{
    /// <summary>
    /// Analyzes <paramref name="syntaxContext"/> when it represents a class that directly or indirectly
    /// inherits from a BlazorCodeFirst base (<c>BodyComponentBase</c> or <c>ChromeLayoutBase</c>), resolving
    /// all symbols from the context's own compilation and classifying its design-time expression
    /// (<c>Body</c> or <c>Chrome</c>) into a template. Returns a symbol-free <see cref="ComponentAnalysis"/>
    /// for every component candidate, including the diagnostic-only shapes that cannot be generated into
    /// (non-partial, nested), or <see langword="null"/> for a node that is not a component at all
    /// (non-inheriting, or declaring no design-time expression of its own).
    /// </summary>
    /// <remarks>
    /// This method must run inside the syntax-provider transform, where the <see cref="SemanticModel"/> and
    /// resolved symbols belong to the current compilation. Its output carries no symbols, so the value that
    /// flows onward stays equatable and cacheable across incremental runs.
    /// </remarks>
    internal static ComponentAnalysis? Analyze(
        GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken)
    {
        var classDeclaration = (ClassDeclarationSyntax)syntaxContext.Node;

        var symbol = syntaxContext.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
        if (symbol is null)
            return null;

        if (DesignTimeBaseFacts.FindDesignTimeBase(symbol) is not { } designTimeBase)
            return null;

        if (DeclaresRenderViewOverride(symbol))
            return null;

        // Body on a component, Chrome on a layout. Resolved from the base symbol so no name is hard-coded.
        var expressionName = DesignTimeBaseFacts.FindDesignTimeExpressionName(symbol);
        if (expressionName is null)
            return null;

        var namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        // Include namespace in the hint name to prevent collisions when two components share
        // the same simple class name across different namespaces.
        var hintName = namespaceName is not null
            ? $"{namespaceName}.{symbol.MetadataName}.g.cs"
            : $"{symbol.MetadataName}.g.cs";

        // One declaration per type owns the generated RenderView. Electing it from the symbol (rather
        // than accepting whichever candidate declaration the syntax provider offered) is what keeps the
        // hint name unique, see FindDesignTimeExpressionDeclaration.
        var elected = FindDesignTimeExpressionDeclaration(symbol, expressionName, cancellationToken);
        if (elected is null || elected.Parent != classDeclaration)
            return null;

        // The three rejections below differ only in which diagnostic they carry: each names the component
        // the same way and each declines to translate. FailureLocation is null in all three because every
        // one of them is located and therefore suppresses Expand's BCF1003, which exists to say "something
        // failed and here is no reason". A member added to ComponentAnalysis is described once here rather
        // than in three blocks that have to be diffed against each other to see that they agree.
        ComponentAnalysis DiagnosticOnly(DiagnosticInfo diagnostic) => new(
            HintName: hintName,
            ClassName: symbol.Name,
            TypeParameters: BuildTypeParameters(symbol),
            Namespace: namespaceName,
            FilePath: classDeclaration.SyntaxTree.FilePath,
            DesignTimeExpressionName: expressionName,
            InheritanceKeys: BuildInheritanceKeys(symbol),
            Template: null,
            BodyDiagnostics: ImmutableArray.Create(diagnostic),
            FailureLocation: null);

        // Emitting into a nested type would mean reproducing the enclosing type chain; unsupported.
        // Reported here rather than at the top of the method so a nested class that merely inherits a
        // BlazorCodeFirst base without declaring the expression is not told that nesting is its problem.
        if (symbol.ContainingType is not null)
        {
            return DiagnosticOnly(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF1005,
                elected.Identifier.GetLocation(),
                [symbol.Name, expressionName]));
        }

        // The generated RenderView joins this class, which requires `partial`. Reported here, from the
        // generator, because an analyzer cannot: no partial modifier means no RenderView, which means
        // CS0534, a declaration-level error, and csc does not run the analyzer driver on a compilation
        // that has one. BCF1001 would be suppressed by the very condition it diagnoses (issue #76).
        // Checked after the election above so a class that declares nothing to generate is never told to
        // add a modifier that would change nothing.
        if (!IsDeclaredPartial(symbol, cancellationToken))
        {
            return DiagnosticOnly(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF1001,
                classDeclaration.Identifier.GetLocation(),
                [symbol.Name, expressionName, designTimeBase.Name]));
        }

        var shape = FindDesignTimeExpression(
            elected, out var bodyExpression, out var bodyStatements, out var getterLocation);

        if (shape == DesignTimeExpressionShape.NoDeclaration)
            return null;

        // A getter that exists but is outside the accepted shapes is reported here rather than left to the
        // bare CS0534 the un-emitted RenderView would raise. Returning an analysis with a null template
        // routes it through Expand's existing dedup, which suppresses BCF1003 when an error is present.
        if (shape == DesignTimeExpressionShape.NotTranslatable)
        {
            // There is also no expression to blame here, the getter never reached one, which is what
            // BCF1004 says.
            return DiagnosticOnly(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF1004,
                getterLocation ?? Location.None,
                [symbol.Name, expressionName]));
        }

        // Resolve the BlazorCodeFirst.Html factory symbols only once the candidate is confirmed to be a
        // component, so unrelated base-listed classes do not pay for the Html type lookup. Resolution is
        // transient to this compilation and never escapes into the cached pipeline.
        var knownSymbols = KnownSymbols.TryCreate(syntaxContext.SemanticModel.Compilation);
        if (knownSymbols is null)
            return null;

        if (bodyExpression is null)
            return null;

        // Reuse the view-part-definition analyzer so component bodies and view part bodies share a
        // single SSC classification. The component body has no parameters, so no parameter holes exist;
        // its access-requirement and diagnostic accumulators are irrelevant here because the generated
        // RenderView is emitted directly into this same component type.
        var bodyContext = new ViewPartBodyContext(
            syntaxContext.SemanticModel,
            symbol,
            expressionName,
            knownSymbols,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            isInlinedAtCallSites: false,
            cancellationToken);

        var template = RenderExpressionAnalyzer.Analyze(bodyStatements, bodyExpression, bodyContext);

        // Translation failed. Sweep the whole expression for the specific cause, an unresolved
        // Component<T>() type argument, a value-position type reference, a misplaced decoration, so the
        // author is told it instead of BCF1003's "not statically analyzable". Only on the failure path, so
        // a healthy body pays nothing. BCF1003 is then suppressed automatically by Expand's error dedup.
        // FailurePathScanners owns the sweep list; both hosts run the same one by construction.
        TemplateLocation? failureLocation = null;
        if (template is null)
        {
            FailurePathScanners.ReportAll(bodyExpression, bodyContext);

            // Carry the innermost expression that failed to classify across the symbol-free boundary so
            // Expand can locate BCF1003. The analyzer records it on every failed classification, and the
            // outermost call is this one, so a failure always leaves something behind; the coalesce is a
            // guard against a future path that produces a null template without going through Analyze.
            failureLocation = TemplateLocation.From(
                bodyContext.UntranslatableLocation ?? bodyExpression.GetLocation());
        }

        // Capture the inheritance chain (self first, then base types) as symbol-free keys so the expander
        // can validate DerivedContainingType access requirements against real inheritance.
        return new ComponentAnalysis(
            HintName: hintName,
            ClassName: symbol.Name,
            TypeParameters: BuildTypeParameters(symbol),
            Namespace: namespaceName,
            FilePath: classDeclaration.SyntaxTree.FilePath,
            DesignTimeExpressionName: expressionName,
            InheritanceKeys: BuildInheritanceKeys(symbol),
            Template: template,
            BodyDiagnostics: bodyContext.Diagnostics.ToImmutable(),
            FailureLocation: failureLocation);
    }

    /// <summary>
    /// Expands a component's analyzed template against the view part <paramref name="registry"/> into a
    /// final <see cref="ComponentModelResult"/>. This is a pure function of value inputs, so it runs after
    /// the registry combine without reintroducing symbols into the pipeline.
    /// </summary>
    internal static ComponentModelResult Expand(
        ComponentAnalysis analysis, ViewPartRegistry registry, CssScopeRegistry cssScopes)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        diagnostics.AddRange(analysis.BodyDiagnostics.AsImmutableArray());

        // An unrecognized/unsupported design-time expression shape yields no template; the abstract
        // RenderView then triggers CS0534 in the user's compilation, which explains nothing on its own.
        if (analysis.Template is null)
        {
            // Emit BCF1003 unless an actionable ERROR was already recorded (e.g. BCF3004/BCF1002). A
            // warning-only design-time expression with a null template still gets BCF1003, so a null
            // template always yields at least one error diagnostic (the S4 invariant). Do NOT gate on
            // Count==0: a co-located BCF3002 warning must not suppress BCF1003.
            // Located at the innermost expression that failed to classify, captured by Analyze. The
            // shapes that reach here with no FailureLocation (non-partial, nested, untranslatable getter)
            // all carry a located error of their own, so the null branch is unreachable rather than a
            // silent fallback to the location-less report this diagnostic used to have (#77).
            if (!diagnostics.Any(static d => d.IsError))
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF1003,
                    analysis.FailureLocation?.ToLocation() ?? Location.None,
                    [DiagnosticDescriptors.DesignTimeExpressionSubject(
                        analysis.ClassName, analysis.DesignTimeExpressionName)]));
            return new ComponentModelResult(null, diagnostics.ToImmutable());
        }

        KeyabilityResolver.CollectForEachContentDiagnostics(analysis.Template, registry, diagnostics);

        var hostCssScope = cssScopes.TryGetScopeForComponentFile(analysis.FilePath, out var scope)
            ? scope
            : null;

        var expansion = ViewPartExpander.Expand(
            analysis.Template,
            registry,
            analysis.InheritanceKeys.AsImmutableArray(),
            cssScopes,
            hostCssScope);
        diagnostics.AddRange(expansion.Diagnostics);

        var hasError = diagnostics.Any(static d => d.IsError);
        if (hasError || expansion.Node is null)
            return new ComponentModelResult(null, diagnostics.ToImmutable());

        var model = new ComponentModel(
            HintName: analysis.HintName,
            ClassName: analysis.ClassName,
            TypeParameters: analysis.TypeParameters,
            Namespace: analysis.Namespace,
            RootNode: expansion.Node);

        return new ComponentModelResult(model, diagnostics.ToImmutable());
    }

    /// <summary>
    /// Returns the generated component's inheritance chain as fully qualified type keys, most-derived
    /// first (the component itself), then each base type up the hierarchy. This is the symbol-free datum
    /// the expander uses to validate protected/private-protected access requirements.
    /// </summary>
    private static ImmutableArray<string> BuildInheritanceKeys(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.BaseType)
            builder.Add(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return builder.ToImmutable();
    }

    /// <summary>
    /// The component's own type-parameter names in declaration order. Names come from the symbol because
    /// CS0264 requires every partial declaration to use the same names in the same order, so the generated
    /// part must not invent its own. Constraints are deliberately not collected: a constraint belongs to
    /// the type parameter rather than to a declaration, so the generated part may omit the clause
    /// entirely, and reproducing it wrongly would reject correct user code.
    /// </summary>
    private static ImmutableArray<string> BuildTypeParameters(INamedTypeSymbol symbol)
    {
        if (symbol.TypeParameters.Length == 0)
            return [];

        var builder = ImmutableArray.CreateBuilder<string>(symbol.TypeParameters.Length);
        foreach (var typeParameter in symbol.TypeParameters)
            builder.Add(typeParameter.Name);

        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Returns the single property declaration that carries the type's design-time expression, or
    /// <see langword="null"/> when the type declares none. Resolved from the symbol rather than from one
    /// candidate declaration for two reasons: a type split across several partial declarations must be
    /// judged once (otherwise two candidates emit the same hint name, which throws inside AddSource and
    /// takes the whole generator down with it), and a partial property's getter lives in its
    /// implementation part while <c>GetMembers</c> returns the definition part.
    /// </summary>
    internal static PropertyDeclarationSyntax? FindDesignTimeExpressionDeclaration(
        INamedTypeSymbol symbol,
        string expressionName,
        CancellationToken cancellationToken)
    {
        if (FindDesignTimeExpressionProperty(symbol, expressionName) is not { } property)
            return null;

        // A partial property's definition part declares `{ get; }` with no body; the getter to translate
        // is in the implementation part. GetMembers only ever returns the definition part.
        var target = property.IsPartialDefinition && property.PartialImplementationPart is { } implementation
            ? implementation
            : property;

        foreach (var syntaxRef in target.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(cancellationToken) is PropertyDeclarationSyntax declaration)
                return declaration;
        }

        return null;
    }

    /// <summary>
    /// True when any of the type's declarations carries the <c>partial</c> modifier. Judged across every
    /// declaration rather than from the one the syntax provider offered, so a type split into a partial
    /// part and a non-partial part (CS0260) is not also told to add a modifier it already has somewhere.
    /// </summary>
    private static bool IsDeclaredPartial(INamedTypeSymbol symbol, CancellationToken cancellationToken)
    {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax(cancellationToken) is ClassDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The concrete design-time expression override this type declares itself, or <see langword="null"/>.
    /// A re-abstraction (<c>abstract override</c>) declares no getter to translate and is therefore not a
    /// declaration, so it earns neither a generated <c>RenderView</c> nor BCF1001.
    /// </summary>
    private static IPropertySymbol? FindDesignTimeExpressionProperty(
        INamedTypeSymbol symbol,
        string expressionName)
    {
        foreach (var member in symbol.GetMembers(expressionName))
        {
            if (member is IPropertySymbol { IsOverride: true, IsAbstract: false } property)
                return property;
        }

        return null;
    }

    /// <summary>The outcome of classifying the component's elected design-time expression declaration.</summary>
    private enum DesignTimeExpressionShape
    {
        /// <summary>
        /// No getter body, and the type is abstract enough that nothing was expected: a re-abstraction
        /// (<c>abstract override</c>). Nothing to translate and nothing to report.
        /// </summary>
        NoDeclaration,

        /// <summary>
        /// An override whose getter reaches one returned expression, either as its whole body or after
        /// statements the generator transplants ahead of it.
        /// </summary>
        Translatable,

        /// <summary>
        /// A concrete override the generator cannot translate: a getter body outside the accepted shapes,
        /// or no getter body at all on a type that needs one (an auto property). Earns BCF1004.
        /// </summary>
        NotTranslatable,
    }

    /// <summary>
    /// Classifies the elected design-time expression declaration. Three getter spellings reduce to a
    /// single expression and are equivalent: the property's own expression body (<c>=&gt; e</c>), the
    /// getter's expression body (<c>get =&gt; e</c>), and a getter block, which
    /// <see cref="RenderExpressionAnalyzer.TryReadTransplantableBlock"/> reads and whose leading statements
    /// it returns in <paramref name="statements"/>. An auto property (no getter body and no
    /// <c>partial</c> modifier) is <see cref="DesignTimeExpressionShape.NotTranslatable"/> and earns
    /// BCF1004. A partial property with no implementation part (<c>partial</c> modifier and no getter
    /// body) is <see cref="DesignTimeExpressionShape.NoDeclaration"/> and is left to CS9248, which names
    /// the property itself. A block that reader refuses is also
    /// <see cref="DesignTimeExpressionShape.NotTranslatable"/> and earns BCF1004.
    /// <para>
    /// The three stay equivalent under the reserved names as well: each asks
    /// <see cref="RenderExpressionAnalyzer.DeclaresReservedName"/> over what it transplants, so a name the
    /// generator cannot rename earns BCF1004 in whichever spelling declared it (#389).
    /// </para>
    /// </summary>
    private static DesignTimeExpressionShape FindDesignTimeExpression(
        PropertyDeclarationSyntax prop,
        out ExpressionSyntax? expression,
        out ImmutableArray<StatementSyntax> statements,
        out Location? location)
    {
        expression = null;
        statements = [];
        location = null;

        // `=> e;`
        if (prop.ExpressionBody is { Expression: var propertyBody })
        {
            // The expression is transplanted under the author's own names, so it holds the reserved set
            // the block spelling holds. Refusing it here is what turns the collision into BCF1004 rather
            // than into C# errors inside the generated file (#389).
            if (RenderExpressionAnalyzer.DeclaresReservedName(propertyBody))
            {
                location = prop.Identifier.GetLocation();
                return DesignTimeExpressionShape.NotTranslatable;
            }

            expression = propertyBody;
            return DesignTimeExpressionShape.Translatable;
        }

        var getter = FindGetAccessor(prop);

        // No getter body at all. An auto property is a concrete override the generator was expected to
        // translate, so it earns BCF1004; a partial declaration part with no implementation is left to
        // CS9248, which names the property itself. The partial check is sound: a partial property's
        // implementation part always has a getter body (CS9250: "A partial property cannot be an
        // auto-property"), so reaching here with `partial` means the definition part with no
        // implementation (left to CS9248), while reaching here without `partial` means an auto property
        // (earns BCF1004).
        if (getter is null || (getter.ExpressionBody is null && getter.Body is null))
        {
            location = prop.Identifier.GetLocation();
            return prop.Modifiers.Any(SyntaxKind.PartialKeyword)
                ? DesignTimeExpressionShape.NoDeclaration
                : DesignTimeExpressionShape.NotTranslatable;
        }

        location = prop.Identifier.GetLocation();

        // `get => e;`
        if (getter.ExpressionBody is { Expression: var accessorBody })
        {
            if (RenderExpressionAnalyzer.DeclaresReservedName(accessorBody))
                return DesignTimeExpressionShape.NotTranslatable;

            expression = accessorBody;
            return DesignTimeExpressionShape.Translatable;
        }

        // `get { return e; }`, and the same block with statements ahead of that return. One reader for
        // both, so the getter and a ForEach content block agree on the shape by construction rather than
        // by two implementations of the same rule.
        if (getter.Body is { } getterBody
            && RenderExpressionAnalyzer.TryReadTransplantableBlock(
                getterBody, out var leading, out var returned))
        {
            expression = returned;
            statements = leading;
            return DesignTimeExpressionShape.Translatable;
        }

        return DesignTimeExpressionShape.NotTranslatable;
    }

    private static AccessorDeclarationSyntax? FindGetAccessor(PropertyDeclarationSyntax prop)
    {
        if (prop.AccessorList is null)
            return null;

        foreach (var accessor in prop.AccessorList.Accessors)
        {
            if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                return accessor;
        }

        return null;
    }

    /// <summary>
    /// True when the component overrides <c>RenderView</c> by hand. Hand-writing it is legal and is the
    /// escape hatch for a body the statically sequenceable subset cannot express, so the generator must
    /// contribute nothing: a second RenderView would be CS0111 raised inside generated code, which the
    /// author cannot fix from their own file. No diagnostic: this is a deliberate choice, not a mistake.
    /// </summary>
    private static bool DeclaresRenderViewOverride(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers("RenderView"))
        {
            if (member is IMethodSymbol { IsOverride: true, IsAbstract: false, Parameters.Length: 1 } method &&
                IsRenderTreeBuilder(method.Parameters[0].Type))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True for <c>Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder</c>. Matched by name
    /// rather than by symbol comparison so no compilation lookup is needed here, following
    /// <see cref="DesignTimeBaseFacts"/>'s approach for the BlazorCodeFirst base types.
    /// </summary>
    private static bool IsRenderTreeBuilder(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "RenderTreeBuilder" } named &&
        named.ContainingNamespace.ToDisplayString() ==
            "Microsoft.AspNetCore.Components.Rendering";
}

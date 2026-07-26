using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BlazorCompose.Compiler.Analysis;
using BlazorCompose.Compiler.Diagnostics;
using BlazorCompose.Compiler.Generation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler;

/// <summary>
/// Turns a candidate class node into an emittable component model in two symbol-free stages:
/// <see cref="Analyze"/> (semantic, runs inside the syntax-provider transform) and
/// <see cref="Expand"/> (a pure value transform combined with the composable registry).
/// </summary>
internal static class ComponentModelFactory
{
    /// <summary>
    /// Analyzes <paramref name="syntaxContext"/> when it represents a partial class that directly or
    /// indirectly inherits from a Compose base (<c>ComposeComponentBase</c> or <c>ComposeLayoutBase</c>),
    /// resolving all symbols from the context's own compilation and classifying its design-time expression
    /// (<c>Body</c> or <c>Chrome</c>) into a template.  Returns a symbol-free <see cref="ComponentAnalysis"/>
    /// for every component candidate, or <see langword="null"/> for a node that is not a generatable
    /// component (non-partial, nested, non-inheriting, or missing the design-time expression).
    /// </summary>
    /// <remarks>
    /// This method must run inside the syntax-provider transform, where the <see cref="SemanticModel"/> and
    /// resolved symbols belong to the current compilation.  Its output carries no symbols, so the value that
    /// flows onward stays equatable and cacheable across incremental runs.
    /// </remarks>
    internal static ComponentAnalysis? Analyze(
        GeneratorSyntaxContext syntaxContext,
        CancellationToken cancellationToken)
    {
        var classDeclaration = (ClassDeclarationSyntax)syntaxContext.Node;

        if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return null;

        var symbol = syntaxContext.SemanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
        if (symbol is null)
            return null;

        // Nested classes require wrapping the generated code inside the outer class hierarchy;
        // that complexity is out of scope for this task.  Skip them so the generator does not
        // emit a structurally incorrect top-level partial class.
        if (symbol.ContainingType is not null)
            return null;

        if (!ComposeComponentBaseFacts.InheritsFromComposeBase(symbol))
            return null;

        // Body on a component, Chrome on a layout. Resolved from the base symbol so no name is hard-coded.
        var expressionName = ComposeComponentBaseFacts.FindDesignTimeExpressionName(symbol);
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

        var shape = FindDesignTimeExpression(
            classDeclaration, expressionName, out var bodyExpression, out var getterLocation);

        if (shape == DesignTimeExpressionShape.Absent)
            return null;

        // A getter that exists but is not a single expression is reported here rather than left to the
        // bare CS0534 the un-emitted RenderView would raise. Returning an analysis with a null template
        // routes it through Expand's existing dedup, which suppresses BC1003 when an error is present.
        if (shape == DesignTimeExpressionShape.NotSingleExpression)
        {
            return new ComponentAnalysis(
                HintName: hintName,
                ClassName: symbol.Name,
                Namespace: namespaceName,
                DesignTimeExpressionName: expressionName,
                InheritanceKeys: BuildInheritanceKeys(symbol),
                Template: null,
                BodyDiagnostics: ImmutableArray.Create(
                    DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC1004,
                        getterLocation ?? Location.None,
                        [symbol.Name, expressionName])));
        }

        // Resolve the BlazorCompose.Html factory symbols only once the candidate is confirmed to be a
        // component, so unrelated base-listed classes do not pay for the Html type lookup.  Resolution is
        // transient to this compilation and never escapes into the cached pipeline.
        var knownSymbols = KnownSymbols.TryCreate(syntaxContext.SemanticModel.Compilation);
        if (knownSymbols is null)
            return null;

        if (bodyExpression is null)
            return null;

        // Reuse the composable-definition analyzer so component bodies and composable bodies share a
        // single SSC classification.  The component body has no parameters, so no parameter holes exist;
        // its access-requirement and diagnostic accumulators are irrelevant here because the generated
        // RenderView is emitted directly into this same component type.
        var bodyContext = new ComposableBodyContext(
            syntaxContext.SemanticModel,
            symbol,
            expressionName,
            knownSymbols,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            cancellationToken);

        var template = RenderExpressionAnalyzer.Analyze(bodyExpression, bodyContext);

        // Capture the inheritance chain (self first, then base types) as symbol-free keys so the expander
        // can validate DerivedContainingType access requirements against real inheritance.
        return new ComponentAnalysis(
            HintName: hintName,
            ClassName: symbol.Name,
            Namespace: namespaceName,
            DesignTimeExpressionName: expressionName,
            InheritanceKeys: BuildInheritanceKeys(symbol),
            Template: template,
            BodyDiagnostics: bodyContext.Diagnostics.ToImmutable());
    }

    /// <summary>
    /// Expands a component's analyzed template against the composable <paramref name="registry"/> into a
    /// final <see cref="ComponentModelResult"/>.  This is a pure function of value inputs, so it runs after
    /// the registry combine without reintroducing symbols into the pipeline.
    /// </summary>
    internal static ComponentModelResult Expand(ComponentAnalysis analysis, ComposableRegistry registry)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        diagnostics.AddRange(analysis.BodyDiagnostics.AsImmutableArray());

        // An unrecognized/unsupported design-time expression shape yields no template; the abstract
        // RenderView then triggers CS0534 in the user's compilation. Add a BlazorCompose-specific BC1003
        // unless the design-time expression already produced an actionable diagnostic (dedup), so the
        // failure is explained rather than opaque.
        if (analysis.Template is null)
        {
            // Emit BC1003 unless an actionable ERROR was already recorded (e.g. BC3004/BC1002). A
            // warning-only design-time expression with a null template still gets BC1003, so a null
            // template always yields at least one error diagnostic (the S4 invariant). Do NOT gate on
            // Count==0: a co-located BC3002 warning must not suppress BC1003.
            if (!diagnostics.Any(static d => d.IsError))
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC1003,
                    Location.None,
                    [analysis.ClassName, analysis.DesignTimeExpressionName]));
            return new ComponentModelResult(null, diagnostics.ToImmutable());
        }

        KeyabilityResolver.CollectForEachContentDiagnostics(analysis.Template, registry, diagnostics);

        var expansion = ComposableExpander.Expand(
            analysis.Template,
            registry,
            analysis.InheritanceKeys.AsImmutableArray());
        diagnostics.AddRange(expansion.Diagnostics);

        var hasError = diagnostics.Any(static d => d.IsError);
        if (hasError || expansion.Node is null)
            return new ComponentModelResult(null, diagnostics.ToImmutable());

        var model = new ComponentModel(
            HintName: analysis.HintName,
            ClassName: analysis.ClassName,
            Namespace: analysis.Namespace,
            RootNode: expansion.Node);

        return new ComponentModelResult(model, diagnostics.ToImmutable());
    }

    /// <summary>
    /// Returns the generated component's inheritance chain as fully qualified type keys, most-derived
    /// first (the component itself), then each base type up the hierarchy.  This is the symbol-free datum
    /// the expander uses to validate protected/private-protected access requirements.
    /// </summary>
    private static ImmutableArray<string> BuildInheritanceKeys(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.BaseType)
            builder.Add(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return builder.ToImmutable();
    }

    /// <summary>The outcome of looking for the component's design-time expression override.</summary>
    private enum DesignTimeExpressionShape
    {
        /// <summary>No override with a getter body: not a generation candidate, and nothing to report.</summary>
        Absent,

        /// <summary>An override whose getter reduces to a single expression.</summary>
        SingleExpression,

        /// <summary>An override with a getter body that does not reduce to a single expression.</summary>
        NotSingleExpression,
    }

    /// <summary>
    /// Classifies the component's design-time expression override.  Three getter spellings reduce to a
    /// single expression and are equivalent: the property's own expression body (<c>=&gt; e</c>), the
    /// getter's expression body (<c>get =&gt; e</c>), and a getter block whose only statement returns an
    /// expression (<c>get { return e; }</c>).  A getter with no body at all — re-abstraction, an auto
    /// property, a partial or extern declaration — is <see cref="DesignTimeExpressionShape.Absent"/>:
    /// there is nothing to translate and nothing to diagnose.  Anything else is
    /// <see cref="DesignTimeExpressionShape.NotSingleExpression"/> and earns BC1004.
    /// </summary>
    private static DesignTimeExpressionShape FindDesignTimeExpression(
        ClassDeclarationSyntax classDecl,
        string expressionName,
        out ExpressionSyntax? expression,
        out Location? location)
    {
        expression = null;
        location = null;

        foreach (var member in classDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax prop)
                continue;

            if (prop.Identifier.Text != expressionName)
                continue;

            if (!prop.Modifiers.Any(SyntaxKind.OverrideKeyword))
                continue;

            // `=> e;`
            if (prop.ExpressionBody is { Expression: var propertyBody })
            {
                expression = propertyBody;
                return DesignTimeExpressionShape.SingleExpression;
            }

            var getter = FindGetAccessor(prop);

            // No getter, or a getter with no body: abstract override, auto property, partial, extern.
            if (getter is null || (getter.ExpressionBody is null && getter.Body is null))
                continue;

            location = prop.Identifier.GetLocation();

            // `get => e;`
            if (getter.ExpressionBody is { Expression: var accessorBody })
            {
                expression = accessorBody;
                return DesignTimeExpressionShape.SingleExpression;
            }

            // `get { return e; }`
            if (getter.Body is { } getterBody
                && getterBody.Statements.Count == 1
                && getterBody.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
            {
                expression = returned;
                return DesignTimeExpressionShape.SingleExpression;
            }

            return DesignTimeExpressionShape.NotSingleExpression;
        }

        return DesignTimeExpressionShape.Absent;
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
}

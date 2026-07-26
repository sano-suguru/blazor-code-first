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

        // Resolve the BlazorCompose.Html factory symbols only once the candidate is confirmed to be a
        // component, so unrelated base-listed classes do not pay for the Html type lookup.  Resolution is
        // transient to this compilation and never escapes into the cached pipeline.
        var knownSymbols = KnownSymbols.TryCreate(syntaxContext.SemanticModel.Compilation);
        if (knownSymbols is null)
            return null;

        var bodyExpression = TryFindDesignTimeExpression(classDeclaration, expressionName);
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

        var namespaceName = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : null;

        // Include namespace in the hint name to prevent collisions when two components share
        // the same simple class name across different namespaces.
        var hintName = namespaceName is not null
            ? $"{namespaceName}.{symbol.MetadataName}.g.cs"
            : $"{symbol.MetadataName}.g.cs";

        // Capture the inheritance chain (self first, then base types) as symbol-free keys so the expander
        // can validate DerivedContainingType access requirements against real inheritance.
        return new ComponentAnalysis(
            HintName: hintName,
            ClassName: symbol.Name,
            Namespace: namespaceName,
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

        // An unrecognized/unsupported Body shape yields no template; the abstract RenderView then triggers
        // CS0534 in the user's compilation. Add a BlazorCompose-specific BC1003 unless the body already
        // produced an actionable diagnostic (dedup), so the failure is explained rather than opaque.
        if (analysis.Template is null)
        {
            // Emit BC1003 unless an actionable ERROR was already recorded (e.g. BC3004/BC1002). A
            // warning-only body with a null template still gets BC1003, so a null template always yields
            // at least one error diagnostic (the S4 invariant). Do NOT gate on Count==0: a co-located
            // BC3002 warning must not suppress BC1003.
            if (!diagnostics.Any(static d => d.IsError))
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC1003,
                    Location.None,
                    [analysis.ClassName]));
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

    /// <summary>
    /// Returns the expression of the component's expression-bodied design-time expression override
    /// (<c>protected override View {expressionName} =&gt; expr;</c>), or <see langword="null"/> when no
    /// such property is present.  Block-bodied getters are intentionally unsupported.
    /// </summary>
    private static ExpressionSyntax? TryFindDesignTimeExpression(
        ClassDeclarationSyntax classDecl, string expressionName)
    {
        foreach (var member in classDecl.Members)
        {
            if (member is not PropertyDeclarationSyntax prop)
                continue;

            if (prop.Identifier.Text != expressionName)
                continue;

            if (!prop.Modifiers.Any(SyntaxKind.OverrideKeyword))
                continue;

            if (prop.ExpressionBody is { Expression: var bodyExpr })
                return bodyExpr;
        }

        return null;
    }
}

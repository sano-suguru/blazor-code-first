using BlazorCodeFirst.Compiler.Analysis;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler;

/// <summary>
/// Incremental source generator entry point.  Discovers Compose base subclasses
/// (<c>BodyComponentBase</c> and <c>ChromeLayoutBase</c>) and emits a <c>RenderView</c> override
/// into the same partial class, and discovers <c>[Composable]</c> definitions into a value-equal
/// registry while reporting declaration-time BCF1002 diagnostics.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class BlazorCodeFirstGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Analyze each candidate component inside the syntax transform: KnownSymbols is resolved
        // transiently from the candidate's own compilation and its design-time expression (Body or
        // Chrome) is classified into a symbol-free template here, so no
        // SemanticModel/ISymbol/Compilation ever flows into the cached pipeline.
        var analyses = context.SyntaxProvider
            .CreateSyntaxProvider(
                // Two admissible shapes. A `partial` class needs no base list: inheritance is judged from
                // the symbol, and a component split across declarations may declare its design-time
                // expression in the part that carries no base list. A non-partial class is admitted only
                // with a base list — it cannot be generated into, but it earns BCF1001, and having exactly
                // one declaration means a Compose base can only reach it through that base list. Testing
                // both here keeps every other class out of the semantic transform.
                static (node, _) => node is ClassDeclarationSyntax declaration &&
                    (declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || declaration.BaseList is not null),
                static (ctx, cancellationToken) => ComponentModelFactory.Analyze(ctx, cancellationToken))
            .Where(static analysis => analysis is not null)
            .WithTrackingName("ComponentAnalysis");

        // Discover [Composable] definitions.  Definition-side SSC analysis resolves KnownSymbols
        // transiently from the definition's compilation so the transform output stays value-equal and
        // free of Roslyn symbols.
        var discoveryResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "BlazorCodeFirst.ComposableAttribute",
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                {
                    var symbols = KnownSymbols.TryCreate(attributeContext.SemanticModel.Compilation);
                    return ComposableDefinitionFactory.Create(attributeContext, symbols, cancellationToken);
                })
            .WithTrackingName("ComposableDiscovery");

        // Report declaration-time diagnostics per definition so unchanged declarations are not
        // re-reported, reconstructing each location from the captured symbol-free coordinates.
        context.RegisterSourceOutput(
            discoveryResults,
            static (productionContext, result) =>
            {
                foreach (var diagnostic in result.Diagnostics)
                    productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            });

        // Collect every source composable entry — including invalid declarations — into a
        // deterministic value-equal registry consumed by call-site expansion.
        var registry = discoveryResults
            .Select(static (result, _) => result.Entry)
            .Collect()
            .Select(static (entries, _) => ComposableRegistry.Create(entries))
            .WithTrackingName("ComposableRegistry");

        // Report BCF3003 for region-rooted ForEach content inside composable definition bodies, once per
        // definition and independent of whether the composable is ever called. Registry-dependent by
        // nature (transitive root-kind resolution), and separate from BCF3002/BCF3004 so their per-definition
        // incrementality is unaffected.
        var composableForEachDiagnostics = registry
            .Select(static (r, _) =>
                (EquatableArray<DiagnosticInfo>)KeyabilityResolver.CollectComposableForEachDiagnostics(r))
            .WithTrackingName("ComposableForEachDiagnostics");

        context.RegisterSourceOutput(
            composableForEachDiagnostics,
            static (productionContext, diagnostics) =>
            {
                foreach (var diagnostic in diagnostics)
                    productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            });

        // Expand each analyzed component against the registry as a pure value transform.  Both inputs are
        // value-equal, so an unchanged rerun is Cached/Unchanged even on the diagnostic branch, and a
        // change to the compose API surface re-runs the transform above and correctly invalidates here.
        var modelResults = analyses
            .Combine(registry)
            .Select(static (input, _) => ComponentModelFactory.Expand(input.Left!, input.Right))
            .WithTrackingName("ComponentModeling");

        // Report model (call-site expansion) diagnostics separately, reconstructing Roslyn diagnostics
        // only inside the output callback.
        context.RegisterSourceOutput(
            modelResults,
            static (productionContext, result) =>
            {
                foreach (var diagnostic in result.Diagnostics)
                    productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
            });

        // Add source only when a final model exists.
        var components = modelResults
            .Select(static (result, _) => result.Model)
            .Where(static model => model is not null);

        context.RegisterSourceOutput(
            components,
            static (productionContext, model) =>
                productionContext.AddSource(model!.HintName, RenderViewEmitter.Emit(model)));
    }
}

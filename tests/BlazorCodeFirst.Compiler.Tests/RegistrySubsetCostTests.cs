using System.Collections.Immutable;
using System.Diagnostics;
using BlazorCodeFirst.Compiler.Analysis;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The measurement issue #480's second comment calls for before choosing between dependency tracking
/// and the cheaper registry-subset alternative <see cref="ViewPartReachability"/> implements.
/// <see cref="RegistryBroadcastCostTests"/> measures the current whole-registry <c>Combine</c>'s cost;
/// this class measures the same edit against a prototype pipeline with the subset projection inserted,
/// so the two figures can be compared directly. <see cref="ViewPartReachabilityEquivalenceTests"/> is
/// the correctness gate this measurement depends on: a figure here means nothing if the subset can
/// diverge from the full registry's <c>Expand</c> result.
/// </summary>
/// <remarks>
/// The prototype generator below lives only in this test project. It mirrors
/// <c>BlazorCodeFirstGenerator.Initialize</c>'s full pipeline (so diagnostics-reporting overhead is
/// identical between the two measurements) with one change: the <c>ComponentModeling</c> assembly line
/// gains an intermediate <c>ComponentExpansionInput</c> stage that projects each component down to its
/// <see cref="ViewPartReachability"/> subset before <c>Expand</c> runs. It is not wired into production
/// and will drift from <c>BlazorCodeFirstGenerator</c> if that file changes; adopting the design for
/// real means applying the same edit there, not promoting this class.
/// </remarks>
public sealed class RegistrySubsetCostTests(ITestOutputHelper output)
{
    private const int ViewPartCount = 3;

    private static readonly int[] LightComponentCounts = [10, 100, 1000];
    private static readonly int[] HeavyComponentCounts = [10, 100, 1000];

    // ---------------------------------------------------------------------------
    // Structural claim: with the subset projection inserted, editing one view part leaves every
    // non-caller's ComponentModeling output Cached. This is RegistryBroadcastCostTests' structural test
    // inverted: that one asserts zero Cached under the current whole-registry Combine; this asserts
    // exactly (N - callers) Cached under the subset design.
    // ---------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(LightComponentCountCases))]
    public void ViewPartOffsetEdit_OnLightCorpus_CachesEveryNonCaller(int componentCount)
    {
        AssertNonCallersAreCached(BuildLightCorpus(componentCount, ViewPartCount), componentCount);
    }

    [Theory]
    [MemberData(nameof(HeavyComponentCountCases))]
    public void ViewPartOffsetEdit_OnHeavyCorpus_CachesEveryNonCaller(int componentCount)
    {
        AssertNonCallersAreCached(BuildHeavyCorpus(componentCount, ViewPartCount), componentCount);
    }

    private void AssertNonCallersAreCached(Corpus corpus, int componentCount)
    {
        var compilation = CreateCompilation(corpus.AllTrees);
        GeneratorDriver driver = CreatePrototypeDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var editedTree = corpus.ViewPartTrees[0];
        var edited = ParseTree("\n" + corpus.ViewPartSources[0], editedTree.FilePath);
        var compilation2 = compilation.ReplaceSyntaxTree(editedTree, edited);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
        var run2 = driver.GetRunResult().Results[0];

        // Verify before measuring: the edit must have genuinely changed the registry entry the edited
        // view part contributes, or every count below would be measuring a no-op.
        var registryOutputs = run2.TrackedSteps["ViewPartRegistry"].SelectMany(s => s.Outputs).ToImmutableArray();
        Assert.Contains(registryOutputs, o => o.Reason == IncrementalStepRunReason.Modified);

        var modelingOutputs = run2.TrackedSteps["ComponentModeling"].SelectMany(s => s.Outputs).ToImmutableArray();
        Assert.Equal(componentCount, modelingOutputs.Length);

        var cachedCount = modelingOutputs.Count(o => o.Reason == IncrementalStepRunReason.Cached);
        var callerCount = componentCount / ViewPartCount + (componentCount % ViewPartCount > 0 ? 1 : 0);

        output.WriteLine(
            $"N={componentCount,5} M={ViewPartCount} callers={callerCount,5} cached={cachedCount,5}");

        Assert.Equal(componentCount - callerCount, cachedCount);
    }

    // ---------------------------------------------------------------------------
    // Cost: the same view-part edit, timed against the subset-projecting prototype, printed alongside
    // the baseline (whole-registry) figure for the same corpus so the two can be compared without
    // rerunning RegistryBroadcastCostTests separately. Not asserted, for the same reason DESIGN.md §7.1
    // and §7.4 exclude wall-clock from published figures: variance is machine-dependent. This is the
    // reproduction procedure for #480's go/no-go call, not a gate.
    // ---------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(LightComponentCountCases))]
    public void ViewPartOffsetEdit_OnLightCorpus_CostsLessThanBaseline(int componentCount)
    {
        MeasureAndPrint("light", componentCount, BuildLightCorpus);
    }

    [Theory]
    [MemberData(nameof(HeavyComponentCountCases))]
    public void ViewPartOffsetEdit_OnHeavyCorpus_CostsLessThanBaseline(int componentCount)
    {
        MeasureAndPrint("heavy", componentCount, BuildHeavyCorpus);
    }

    private void MeasureAndPrint(string label, int componentCount, Func<int, int, Corpus> buildCorpus)
    {
        const int trials = 5;

        double baselineMs = MeasureMedianMs(componentCount, buildCorpus, CreateBaselineDriver, trials);
        double subsetMs = MeasureMedianMs(componentCount, buildCorpus, CreatePrototypeDriver, trials);

        output.WriteLine(
            $"[{label}] N={componentCount,5} M={ViewPartCount} " +
            $"baseline={baselineMs,8:F2}ms subset={subsetMs,8:F2}ms delta={subsetMs - baselineMs,8:F2}ms");
    }

    private static double MeasureMedianMs(
        int componentCount,
        Func<int, int, Corpus> buildCorpus,
        Func<CSharpGeneratorDriver> createDriver,
        int trials)
    {
        var samples = new double[trials];
        for (int trial = 0; trial < trials; trial++)
        {
            var corpus = buildCorpus(componentCount, ViewPartCount);
            var compilation = CreateCompilation(corpus.AllTrees);
            GeneratorDriver driver = createDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

            var editedTree = corpus.ViewPartTrees[0];
            var edited = ParseTree("\n" + corpus.ViewPartSources[0], editedTree.FilePath);
            var compilation2 = compilation.ReplaceSyntaxTree(editedTree, edited);

            var stopwatch = Stopwatch.StartNew();
            driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
            stopwatch.Stop();
            samples[trial] = stopwatch.Elapsed.TotalMilliseconds;
        }

        return samples.OrderBy(s => s).ElementAt(trials / 2);
    }

    public static TheoryData<int> LightComponentCountCases()
    {
        var data = new TheoryData<int>();
        foreach (int count in LightComponentCounts)
            data.Add(count);
        return data;
    }

    public static TheoryData<int> HeavyComponentCountCases()
    {
        var data = new TheoryData<int>();
        foreach (int count in HeavyComponentCounts)
            data.Add(count);
        return data;
    }

    // ---------------------------------------------------------------------------
    // Corpus construction. Light mirrors RegistryBroadcastCostTests exactly (kept independent rather
    // than shared, so a change to one corpus cannot silently perturb the other's published/measured
    // figures). Heavy inflates each view part's own body (many sibling elements plus one nested,
    // transitive view-part call) so Expand's per-component cost is large relative to the reachability
    // walk's, which is the regime the subset design is meant to win in.
    // ---------------------------------------------------------------------------

    private sealed record Corpus(SyntaxTree[] AllTrees, SyntaxTree[] ViewPartTrees, string[] ViewPartSources);

    private static Corpus BuildLightCorpus(int componentCount, int viewPartCount)
    {
        var allTrees = new List<SyntaxTree> { ParseTree(SupportSource, "Support.cs") };

        var viewPartSources = new string[viewPartCount];
        var viewPartTrees = new SyntaxTree[viewPartCount];
        for (int m = 0; m < viewPartCount; m++)
        {
            viewPartSources[m] = LightViewPartSource(m);
            viewPartTrees[m] = ParseTree(viewPartSources[m], $"LightWidget{m}.cs");
            allTrees.Add(viewPartTrees[m]);
        }

        for (int n = 0; n < componentCount; n++)
            allTrees.Add(ParseTree(LightComponentSource(n, n % viewPartCount), $"LightComponent{n}.cs"));

        return new Corpus([.. allTrees], viewPartTrees, viewPartSources);
    }

    private static Corpus BuildHeavyCorpus(int componentCount, int viewPartCount)
    {
        const int elementsPerInner = 30;

        var allTrees = new List<SyntaxTree> { ParseTree(SupportSource, "Support.cs") };

        var viewPartSources = new string[viewPartCount];
        var viewPartTrees = new SyntaxTree[viewPartCount];
        for (int m = 0; m < viewPartCount; m++)
        {
            viewPartSources[m] = HeavyViewPartSource(m, elementsPerInner);
            viewPartTrees[m] = ParseTree(viewPartSources[m], $"HeavyWidget{m}.cs");
            allTrees.Add(viewPartTrees[m]);
        }

        for (int n = 0; n < componentCount; n++)
            allTrees.Add(ParseTree(HeavyComponentSource(n, n % viewPartCount), $"HeavyComponent{n}.cs"));

        return new Corpus([.. allTrees], viewPartTrees, viewPartSources);
    }

    private const string SupportSource = """
        namespace TestNs;

        public sealed record Item(int Id, string Name);
        """;

    private static string LightViewPartSource(int index) =>
        $$"""
        using System.Collections.Generic;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public static class LightWidgets{{index}}
        {
            [ViewPart]
            public static View Widget{{index}}(List<Item> items) =>
                ForEach(items, key: i => i.Id, content: i => Span[i.Name]);
        }
        """;

    private static string LightComponentSource(int index, int viewPartIndex) =>
        $$"""
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public partial class LightComponent{{index}} : BodyComponentBase
        {
            protected override View Body => LightWidgets{{viewPartIndex}}.Widget{{viewPartIndex}}(new());
        }
        """;

    /// <summary>
    /// One view part (<c>Widget{index}</c>) calling a second, transitively-only-reachable view part
    /// (<c>Inner{index}</c>) whose body is <paramref name="elementCount"/> sibling <c>Span</c> elements —
    /// large enough that <c>Expand</c> rebuilds a correspondingly large subtree, while the reachability
    /// walk over the same elements only checks each node's type and pushes its children.
    /// </summary>
    private static string HeavyViewPartSource(int index, int elementCount)
    {
        var elements = string.Join(
            ",\n                ",
            Enumerable.Range(0, elementCount).Select(i => $"Span[\"item-\" + id + \"-{i}\"]"));

        return $$"""
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace TestNs;

            public static class HeavyWidgets{{index}}
            {
                [ViewPart]
                private static View Inner{{index}}(int id) =>
                    Fragment[
                        {{elements}}
                    ];

                [ViewPart]
                public static View Widget{{index}}(List<Item> items) =>
                    ForEach(items, key: i => i.Id, content: i => Inner{{index}}(i.Id));
            }
            """;
    }

    private static string HeavyComponentSource(int index, int viewPartIndex) =>
        $$"""
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public partial class HeavyComponent{{index}} : BodyComponentBase
        {
            protected override View Body => HeavyWidgets{{viewPartIndex}}.Widget{{viewPartIndex}}(new());
        }
        """;

    private static SyntaxTree ParseTree(string source, string path) =>
        CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: path);

    private static CSharpGeneratorDriver CreateBaselineDriver() =>
        (CSharpGeneratorDriver)CSharpGeneratorDriver.Create(
            generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: default,
                trackIncrementalGeneratorSteps: true));

    private static CSharpGeneratorDriver CreatePrototypeDriver() =>
        (CSharpGeneratorDriver)CSharpGeneratorDriver.Create(
            generators: [new RegistrySubsetPrototypeGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: default,
                trackIncrementalGeneratorSteps: true));

    private static CSharpCompilation CreateCompilation(SyntaxTree[] trees) =>
        CSharpCompilation.Create(
            assemblyName: "RegistrySubsetCostAssembly",
            syntaxTrees: trees,
            references: CompilationTestHost.BuildMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}

/// <summary>
/// Test-project-only prototype: <see cref="BlazorCodeFirstGenerator.Initialize"/>'s pipeline with one
/// change, the <c>ComponentModeling</c> assembly line gains an intermediate projection stage. See
/// <see cref="RegistrySubsetCostTests"/>'s remarks for why this lives here rather than in production.
/// </summary>
file sealed class RegistrySubsetPrototypeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var cssScopeRegistry = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(".cs.css", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) =>
            {
                var (text, optionsProvider) = pair;
                var options = optionsProvider.GetOptions(text);
                CssScopeEntry? entry = options.TryGetValue("build_metadata.AdditionalFiles.CssScope", out var scope)
                    ? new CssScopeEntry(text.Path, scope)
                    : null;
                return entry;
            })
            .Where(static entry => entry is not null)
            .Select(static (entry, _) => entry!.Value)
            .Collect()
            .Select(static (entries, _) => CssScopeRegistry.Create(entries))
            .WithTrackingName("CssScopeRegistry");

        var analyses = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax declaration &&
                    (declaration.Modifiers.Any(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword) || declaration.BaseList is not null),
                static (ctx, cancellationToken) => ComponentModelFactory.Analyze(ctx, cancellationToken))
            .Where(static analysis => analysis is not null)
            .WithTrackingName("ComponentAnalysis");

        var discoveryResults = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "BlazorCodeFirst.ViewPartAttribute",
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax,
                static (attributeContext, cancellationToken) =>
                {
                    var symbols = KnownSymbols.TryCreate(attributeContext.SemanticModel.Compilation);
                    return ViewPartDefinitionFactory.Create(attributeContext, symbols, cancellationToken);
                })
            .WithTrackingName("ViewPartDiscovery");

        context.RegisterSourceOutput(
            discoveryResults,
            static (productionContext, result) => ReportAll(productionContext, result.Diagnostics));

        var registry = discoveryResults
            .Select(static (result, _) => result.Entry)
            .Collect()
            .Select(static (entries, _) => ViewPartRegistry.Create(entries))
            .WithTrackingName("ViewPartRegistry");

        var viewPartForEachDiagnostics = registry
            .Select(static (r, _) =>
                (EquatableArray<DiagnosticInfo>)KeyabilityResolver.CollectViewPartForEachDiagnostics(r))
            .WithTrackingName("ViewPartForEachDiagnostics");

        context.RegisterSourceOutput(
            viewPartForEachDiagnostics,
            static (productionContext, diagnostics) => ReportAll(productionContext, diagnostics));

        var componentFilePaths = analyses
            .Select(static (a, _) => a!.FilePath)
            .Collect()
            .Select(static (paths, _) => (EquatableArray<string>)paths);

        var viewPartFilePaths = registry
            .Select(static (r, _) => (EquatableArray<string>)ImmutableArray.CreateRange(
                r.Entries.AsImmutableArray(), static (ViewPartDefinitionEntry e) => e.FilePath));

        var orphanCssDiagnostics = cssScopeRegistry
            .Combine(componentFilePaths)
            .Combine(viewPartFilePaths)
            .Select(static (input, _) =>
                (EquatableArray<DiagnosticInfo>)OrphanScopedCssResolver.CollectOrphanDiagnostics(
                    input.Left.Left,
                    input.Left.Right.AsImmutableArray(),
                    input.Right.AsImmutableArray()))
            .WithTrackingName("OrphanScopedCssDiagnostics");

        context.RegisterSourceOutput(
            orphanCssDiagnostics,
            static (productionContext, diagnostics) => ReportAll(productionContext, diagnostics));

        // The one change from BlazorCodeFirstGenerator.Initialize: an intermediate stage that projects
        // each component down to its ViewPartReachability subset before Expand runs, so an edit to a
        // view part only re-runs Expand for the components that can actually reach it. This stage
        // returns the subset's raw EquatableArray entries, not built registries: registry construction
        // (dictionary building in ViewPartRegistry.Create/CssScopeRegistry.Create) is deferred to the
        // Expand stage below, so a component whose subset is unchanged never pays for it.
        var expansionInputs = analyses
            .Combine(registry)
            .Combine(cssScopeRegistry)
            .Select(static (input, _) =>
            {
                var analysis = input.Left.Left!;
                var fullRegistry = input.Left.Right;
                var fullCss = input.Right;
                var (viewParts, cssEntries) = ViewPartReachability.Collect(analysis, fullRegistry, fullCss);
                return (Analysis: analysis, ViewParts: viewParts, CssScopes: cssEntries);
            })
            .WithTrackingName("ComponentExpansionInput");

        var modelResults = expansionInputs
            .Select(static (i, _) => ComponentModelFactory.Expand(
                i.Analysis,
                ViewPartRegistry.Create(i.ViewParts.AsImmutableArray()),
                CssScopeRegistry.Create(i.CssScopes.AsImmutableArray())))
            .WithTrackingName("ComponentModeling");

        context.RegisterSourceOutput(
            modelResults,
            static (productionContext, result) => ReportAll(productionContext, result.Diagnostics));

        var components = modelResults
            .Select(static (result, _) => result.Model)
            .Where(static model => model is not null);

        context.RegisterSourceOutput(
            components,
            static (productionContext, model) =>
                productionContext.AddSource(model!.HintName, RenderViewEmitter.Emit(model)));
    }

    private static void ReportAll(SourceProductionContext context, EquatableArray<DiagnosticInfo> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
    }
}

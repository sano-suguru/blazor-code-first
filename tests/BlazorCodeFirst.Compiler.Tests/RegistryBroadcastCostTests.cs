using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The measurement behind #480: editing a single <c>[ViewPart]</c> definition combines onto
/// <c>analyses</c> in <c>BlazorCodeFirstGenerator.modelResults</c>, so <c>ComponentModelFactory.Expand</c>
/// re-runs for every component in the compilation, not only the ones that call the edited view part.
/// Each test asserts the structural claim (which components actually recompute) and prints the
/// wall-clock figures DESIGN.md §7 declines to publish as a comparison target, following the same
/// "print, don't assert" split <c>DiffCostTests</c> uses for timing.
/// </summary>
public sealed class RegistryBroadcastCostTests(ITestOutputHelper output)
{
    private const int ViewPartCount = 3;
    private static readonly int[] ComponentCounts = [10, 100, 1000];

    /// <summary>
    /// Structural claim: a formatting-only edit to one view part file (a leading blank line, which
    /// shifts every <c>TemplateLocation</c> offset in that file but changes no semantics) leaves no
    /// <c>ComponentModeling</c> output Cached, regardless of how many of the N components actually call
    /// the edited view part. Cached is the only reason that would mean Expand did not re-run for that
    /// component, so "zero Cached" is exactly the over-invalidation #480 describes; it does not claim
    /// every component reports Unchanged, since the few callers of the edited view part legitimately
    /// report Modified.
    /// </summary>
    [Theory]
    [MemberData(nameof(ComponentCountCases))]
    public void ViewPartOffsetEdit_RecomputesEveryComponent_NoneCached(int componentCount)
    {
        var corpus = BuildCorpus(componentCount, ViewPartCount);
        var compilation = CreateCompilation(corpus.AllTrees);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var editedTree = corpus.ViewPartTrees[0];
        var edited = ParseTree("\n" + corpus.ViewPartSources[0], editedTree.FilePath);
        var compilation2 = compilation.ReplaceSyntaxTree(editedTree, edited);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
        var run2 = driver.GetRunResult().Results[0];

        // Verify before measuring (DESIGN.md §7's discipline): the edit must actually have produced a
        // structurally different registry, or every figure below would be measuring a no-op.
        var registryOutputs = run2.TrackedSteps["ViewPartRegistry"].SelectMany(s => s.Outputs).ToImmutableArray();
        Assert.Contains(registryOutputs, o => o.Reason == IncrementalStepRunReason.Modified);

        var modelingOutputs = run2.TrackedSteps["ComponentModeling"].SelectMany(s => s.Outputs).ToImmutableArray();
        Assert.Equal(componentCount, modelingOutputs.Length);

        var byReason = modelingOutputs
            .GroupBy(o => o.Reason)
            .ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine(
            $"N={componentCount,5} M={ViewPartCount} " +
            $"reasons: {string.Join(" ", byReason.OrderBy(p => p.Key.ToString()).Select(p => $"{p.Key}={p.Value}"))}");

        Assert.DoesNotContain(modelingOutputs, o => o.Reason == IncrementalStepRunReason.Cached);
    }

    /// <summary>
    /// Cost: wall-clock for the same view-part edit as above, against a same-shape control that edits an
    /// unrelated component file instead (an offset-only edit that never touches the registry, so it
    /// isolates the driver's own per-edit overhead). The delta between the two is what the registry
    /// broadcast itself costs; not asserted, since DESIGN.md §7.1 excludes wall-clock from published
    /// figures due to machine-dependent variance, but printed so this test's output is the reproduction
    /// procedure for a go/no-go call on the dependency-tracking redesign #480 describes.
    /// </summary>
    [Theory]
    [MemberData(nameof(ComponentCountCases))]
    public void ViewPartOffsetEdit_CostsMoreThanAnUnrelatedOffsetEdit(int componentCount)
    {
        const int trials = 5;

        double viewPartEditMs = MeasureMedianMs(componentCount, editViewPart: true, trials);
        double controlEditMs = MeasureMedianMs(componentCount, editViewPart: false, trials);

        output.WriteLine(
            $"N={componentCount,5} M={ViewPartCount} " +
            $"viewPartEdit={viewPartEditMs,8:F2}ms controlEdit={controlEditMs,8:F2}ms " +
            $"delta={viewPartEditMs - controlEditMs,8:F2}ms");
    }

    public static TheoryData<int> ComponentCountCases()
    {
        var data = new TheoryData<int>();
        foreach (int count in ComponentCounts)
            data.Add(count);
        return data;
    }

    private static double MeasureMedianMs(int componentCount, bool editViewPart, int trials)
    {
        var samples = new double[trials];
        for (int trial = 0; trial < trials; trial++)
        {
            var corpus = BuildCorpus(componentCount, ViewPartCount);
            var compilation = CreateCompilation(corpus.AllTrees);
            GeneratorDriver driver = CreateDriver();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

            Compilation compilation2;
            if (editViewPart)
            {
                var editedTree = corpus.ViewPartTrees[0];
                var edited = ParseTree("\n" + corpus.ViewPartSources[0], editedTree.FilePath);
                compilation2 = compilation.ReplaceSyntaxTree(editedTree, edited);
            }
            else
            {
                var editedTree = corpus.ComponentTrees[0];
                var edited = ParseTree("\n" + corpus.ComponentSources[0], editedTree.FilePath);
                compilation2 = compilation.ReplaceSyntaxTree(editedTree, edited);
            }

            var stopwatch = Stopwatch.StartNew();
            driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
            stopwatch.Stop();
            samples[trial] = stopwatch.Elapsed.TotalMilliseconds;
        }

        return samples.OrderBy(s => s).ElementAt(trials / 2);
    }

    // ---------------------------------------------------------------------------
    // Corpus construction: N components x M view parts. Each view part has a ForEach body so its
    // ViewPartDefinitionEntry carries a TemplateLocation, making the registry sensitive to offset-only
    // edits (the same distinction IncrementalGeneratorTests.WhenAnEditOnlyShiftsOffsets_... documents:
    // a body with no ForEach/[ViewPart] call carries no such offset, and this corpus deliberately does).
    // ---------------------------------------------------------------------------

    private sealed record Corpus(
        SyntaxTree[] AllTrees,
        SyntaxTree[] ViewPartTrees,
        string[] ViewPartSources,
        SyntaxTree[] ComponentTrees,
        string[] ComponentSources);

    private static Corpus BuildCorpus(int componentCount, int viewPartCount)
    {
        var allTrees = new System.Collections.Generic.List<SyntaxTree> { ParseTree(SupportSource, "Support.cs") };

        var viewPartSources = new string[viewPartCount];
        var viewPartTrees = new SyntaxTree[viewPartCount];
        for (int m = 0; m < viewPartCount; m++)
        {
            viewPartSources[m] = ViewPartSource(m);
            viewPartTrees[m] = ParseTree(viewPartSources[m], $"Widget{m}.cs");
            allTrees.Add(viewPartTrees[m]);
        }

        var componentSources = new string[componentCount];
        var componentTrees = new SyntaxTree[componentCount];
        for (int n = 0; n < componentCount; n++)
        {
            componentSources[n] = ComponentSource(n, n % viewPartCount);
            componentTrees[n] = ParseTree(componentSources[n], $"Component{n}.cs");
            allTrees.Add(componentTrees[n]);
        }

        return new Corpus(
            [.. allTrees], viewPartTrees, viewPartSources, componentTrees, componentSources);
    }

    private const string SupportSource = """
        namespace TestNs;

        public sealed record Item(int Id, string Name);
        """;

    private static string ViewPartSource(int index) =>
        $$"""
        using System.Collections.Generic;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public static class Widgets{{index}}
        {
            [ViewPart]
            public static View Widget{{index}}(List<Item> items) =>
                ForEach(items, key: i => i.Id, content: i => Span[i.Name]);
        }
        """;

    private static string ComponentSource(int index, int viewPartIndex) =>
        $$"""
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public partial class Component{{index}} : BodyComponentBase
        {
            protected override View Body => Widgets{{viewPartIndex}}.Widget{{viewPartIndex}}(new());
        }
        """;

    private static SyntaxTree ParseTree(string source, string path) =>
        CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: path);

    private static CSharpGeneratorDriver CreateDriver() =>
        (CSharpGeneratorDriver)CSharpGeneratorDriver.Create(
            generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: default,
                trackIncrementalGeneratorSteps: true));

    private static CSharpCompilation CreateCompilation(SyntaxTree[] trees)
    {
        var references = CompilationTestHost.BuildMetadataReferences();
        return CSharpCompilation.Create(
            assemblyName: "RegistryBroadcastCostAssembly",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

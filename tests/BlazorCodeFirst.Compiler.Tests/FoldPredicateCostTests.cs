using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The measurement Issue #149 asks for before any change is made: whether
/// <c>StaticMarkupSerializer.IsFoldable</c>'s recursive per-ancestor re-walk shows up at realistic
/// component sizes. Two shapes isolate two different costs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape A</b> (<see cref="SingleDynamicRootChainBody"/>) is a single-child chain with one dynamic
/// attribute at the root and nothing but static elements below it. Because each level has exactly one
/// child, the subtree's size grows with its depth, so this shape turns the issue's own claim —
/// "a subtree at depth d is walked roughly d times" — into a direct, falsifiable prediction: if the
/// claim holds, generation time should grow worse than linearly as depth increases; if <c>IsFoldable</c>
/// really is called only once per node, it should grow linearly.
/// </para>
/// <para>
/// <b>Shape B</b> (<see cref="DistributedSpineBody"/> vs. <see cref="SingleBlockBody"/>) holds the total
/// static leaf count fixed and asks whether splitting it across many non-foldable ancestors (the "page
/// chrome carrying one dynamic attribute at the top" shape the issue names) costs more than the same
/// volume folded once under a single dynamic wrapper.
/// </para>
/// <para>
/// Following <c>RegistryBroadcastCostTests</c>' split: a structural test first confirms each corpus
/// actually produces the fold pattern the cost tests assume (DESIGN.md §7's "verify before measuring"),
/// and the cost tests print wall-clock figures without asserting on them, since DESIGN.md §7.1
/// deliberately excludes machine-dependent timings from published figures.
/// </para>
/// </remarks>
public sealed class FoldPredicateCostTests(ITestOutputHelper output)
{
    private const string DynFieldDecl = "private string _dyn => \"d\";";

    // ---------------------------------------------------------------------------
    // Structural checks: the premise the cost tests below depend on.
    // ---------------------------------------------------------------------------

    [Fact]
    public void SingleDynamicRootChain_FoldsTheWholeStaticSubtreeIntoOneFrame()
    {
        var code = Generate(SingleDynamicRootChainBody(depth: 20));

        Assert.Equal(1, CountOccurrences(code, "AddMarkupContent("));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    public void DistributedSpine_FoldsOneFramePerLevel(int depth)
    {
        var code = Generate(DistributedSpineBody(depth, leavesPerLevel: 5));

        Assert.Equal(depth, CountOccurrences(code, "AddMarkupContent("));
    }

    [Fact]
    public void SingleBlock_FoldsIntoOneFrame()
    {
        var code = Generate(SingleBlockBody(totalLeaves: 50));

        Assert.Equal(1, CountOccurrences(code, "AddMarkupContent("));
    }

    // ---------------------------------------------------------------------------
    // Cost: printed, not asserted (DESIGN.md §7's discipline for machine-dependent figures). This is the
    // reproduction procedure for a go/no-go call on memoizing IsFoldable, not a published number.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Shape A. If <c>IsFoldable</c> costs more than one walk per node, doubling depth should cost
    /// noticeably more than double, since the same shape makes size grow with depth.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(200)]
    public void SingleDynamicRootChain_GenerationTimeByDepth(int depth)
    {
        var ms = MeasureMedianMs(SingleDynamicRootChainBody(depth), trials: 5);

        output.WriteLine($"chain depth={depth,5} time={ms,8:F3}ms");
    }

    /// <summary>
    /// Shape B. Same total static leaf count either concentrated under one dynamic wrapper or spread
    /// across <paramref name="depth"/> dynamic wrappers. A ratio near 1 says splitting costs nothing
    /// beyond the volume itself; a ratio that grows with depth says the per-level redundancy is real.
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    public void DistributedSpine_VersusSingleBlock_SameTotalVolume(int depth)
    {
        const int totalLeaves = 200;
        var leavesPerLevel = totalLeaves / depth;

        var distributedMs = MeasureMedianMs(DistributedSpineBody(depth, leavesPerLevel), trials: 5);
        var singleBlockMs = MeasureMedianMs(SingleBlockBody(leavesPerLevel * depth), trials: 5);

        output.WriteLine(
            $"depth={depth,4} leaves={leavesPerLevel * depth,5} "
                + $"distributed={distributedMs,8:F3}ms singleBlock={singleBlockMs,8:F3}ms "
                + $"ratio={distributedMs / singleBlockMs,6:F2}x");
    }

    // ---------------------------------------------------------------------------
    // Corpus
    // ---------------------------------------------------------------------------

    /// <summary>
    /// One dynamic attribute at the root, then <paramref name="depth"/> levels of single-child static
    /// nesting below it. Total static node count grows with depth, by construction.
    /// </summary>
    private static string SingleDynamicRootChainBody(int depth)
    {
        var inner = "Span[\"leaf\"]";
        for (var i = 0; i < depth; i++)
            inner = $"Div[{inner}]";

        return $"Div.Attr(\"data-root\", _dyn)[{inner}]";
    }

    /// <summary>
    /// <paramref name="depth"/> nested dynamic wrappers ("page chrome"), each carrying
    /// <paramref name="leavesPerLevel"/> static siblings alongside the continuation of the chain.
    /// </summary>
    private static string DistributedSpineBody(int depth, int leavesPerLevel)
    {
        var inner = "Span[\"end\"]";
        for (var level = depth - 1; level >= 0; level--)
        {
            var leaves = string.Join(
                ", ", Enumerable.Range(0, leavesPerLevel).Select(j => $"Span[\"s{level}_{j}\"]"));
            var children = leavesPerLevel > 0 ? $"{leaves}, {inner}" : inner;
            inner = $"Div.Attr(\"data-x{level}\", _dyn)[{children}]";
        }

        return inner;
    }

    /// <summary>The same total leaf count as <see cref="DistributedSpineBody"/>, under one dynamic root.</summary>
    private static string SingleBlockBody(int totalLeaves)
    {
        var leaves = string.Join(", ", Enumerable.Range(0, totalLeaves).Select(j => $"Span[\"s{j}\"]"));

        return $"Div.Attr(\"data-root\", _dyn)[{leaves}]";
    }

    private static string ComponentSource(string body) =>
        $$"""
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            {{DynFieldDecl}}
            protected override View Body => {{body}};
        }
        """;

    private static string Generate(string body)
    {
        var result = CompilationTestHost.RunGenerator(ComponentSource(body));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);

        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static double MeasureMedianMs(string body, int trials)
    {
        var source = ComponentSource(body);
        var samples = new double[trials];

        for (var trial = 0; trial < trials; trial++)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                path: "Test.cs");
            var compilation = CompilationTestHost.CreateCompilation(syntaxTree);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
                driverOptions: new GeneratorDriverOptions(
                    disabledOutputs: default,
                    trackIncrementalGeneratorSteps: true));

            var stopwatch = Stopwatch.StartNew();
            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            stopwatch.Stop();
            samples[trial] = stopwatch.Elapsed.TotalMilliseconds;
        }

        return samples.OrderBy(s => s).ElementAt(trials / 2);
    }
}

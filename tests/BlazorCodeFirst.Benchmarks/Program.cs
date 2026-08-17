using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BlazorCodeFirst.Benchmarks.Components;
using BlazorCodeFirst.IntegrationTests.Components;
using BlazorCodeFirst.IntegrationTests.DiffCost;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.Benchmarks;

internal static class Program
{
    private static int Main(string[] args)
    {
        // Architecture gate, on the same terms as TrimTests' missing-publish check: a number from a
        // comparison whose two sides render different frames would be attributable to the mismatch
        // rather than to the compilation strategy, so refuse to produce one.
        var generated = new RenderTreeBuilder();
        var razor = new RenderTreeBuilder();
        new BenchmarkView { Count = 7 }.Build(generated);
        new BenchmarkViewRazor { Count = 7 }.Build(razor);

        if (FramesDiffer(generated, razor,
            "BenchmarkView and BenchmarkViewRazor do not render equivalent frames, so an " +
            "allocation comparison between them would not measure what DESIGN.md §7.1 claims:"))
        {
            return 1;
        }

        // The §7.1 dynamic-content-path pair: the generated Opaque emission against a directly
        // hand-written RenderFragment call. Same reasoning as the gate above.
        var opaqueGenerated = new RenderTreeBuilder();
        var opaqueRazor = new RenderTreeBuilder();
        new OpaqueContentView { Count = 7 }.Build(opaqueGenerated);
        new OpaqueContentViewRazor { Count = 7 }.Build(opaqueRazor);

        if (FramesDiffer(opaqueGenerated, opaqueRazor,
            "OpaqueContentView and OpaqueContentViewRazor do not render equivalent frames, so an " +
            "allocation comparison between them would not measure what DESIGN.md §7.1 claims:"))
        {
            return 1;
        }

        // The same comparison in the static spelling. This one guards a claim rather than a figure: it
        // carries no benchmark, because §7.1's published allocations were measured against the
        // property-driven pair above and re-spelling those would invalidate published numbers. What it
        // proves is that DESIGN.md §7.1's statement — that since #140 the two compilers emit the same
        // frames for a fully static subtree — holds mechanically. It fails the run like the gates above
        // rather than warning, because a claim nothing checks is the failure mode #140 hit repeatedly.
        var staticGenerated = new RenderTreeBuilder();
        var staticRazor = new RenderTreeBuilder();
        new StaticParityView().Build(staticGenerated);
        new StaticParityViewRazor().Build(staticRazor);

        if (FramesDiffer(staticGenerated, staticRazor,
            "StaticParityView and StaticParityViewRazor do not render equivalent frames, so " +
            "DESIGN.md §7.1's claim that the static fold reaches Razor's frame shape no longer holds:"))
        {
            return 1;
        }

        Console.WriteLine(
            "Static spelling parity: StaticParityView and StaticParityViewRazor both render " +
            $"{staticGenerated.GetFrames().Count} frame(s), equivalent apart from sequence numbers.");

        // The #140 fold pairs used to fail the gate above by construction: before the emitter folded,
        // the element spelling was the unfolded baseline and the markup spelling was a hand-written
        // Html.Raw stand-in for the shape folding was expected to produce, so the two sides' frame
        // counts differed on purpose. Now that #140 folds static runs, the element spelling reaches
        // that same shape itself, so the condition this gate needs is equality: if the two sides'
        // frame counts diverge, either the element spelling stopped folding or the pair no longer
        // describes the same DOM, and either way a ratio between them would not measure folding. The
        // pre-fold counts (23 and 12) are kept here only as reference points for how much folding
        // used to save; they are not asserted, since re-checking them would need a non-folding twin
        // fixture with the same DOM, which FoldFixtureTests deliberately does not add (see its
        // remarks).
        (string Name, int Element, int Markup, int PreFoldElement)[] foldPairs =
        [
            ("static-heavy",
                FrameCount(builder => new StaticHeavyElementView().Build(builder)),
                FrameCount(builder => new StaticHeavyMarkupView().Build(builder)),
                23),
            ("low-static",
                FrameCount(builder => new MixedElementView().Build(builder)),
                FrameCount(builder => new MixedMarkupView().Build(builder)),
                12),
        ];

        foreach (var pair in foldPairs)
        {
            Console.WriteLine(
                $"#140 fold pair '{pair.Name}': both sides fold to {pair.Element} frames " +
                $"(element was {pair.PreFoldElement} before #140).");

            if (pair.Markup != pair.Element)
            {
                Console.Error.WriteLine(
                    $"The '{pair.Name}' fold pair's element spelling emits {pair.Element} frames " +
                    $"against the markup spelling's {pair.Markup} after folding, so the element " +
                    "spelling did not fold to the markup spelling's shape and the #140 comparison " +
                    "would not measure folding.");
                return 1;
            }
        }

        // #239's two candidates, on the same terms as the gates above: a ratio between two joins that
        // answer different text would measure the disagreement rather than where the join belongs.
        // Every shape ClassChannelBenchmarks times, plus the all-null one it does not, which is the
        // only shape where the two could differ without differing on any of the others.
        (string? Generated, string?[] Terms)[] joinShapes =
        [
            (ClassJoinCandidates.Generated("card", "wide"), ["card", "wide"]),
            (ClassJoinCandidates.Generated("card", null), ["card", null]),
            (ClassJoinCandidates.Generated(null, null), [null, null]),
            (ClassJoinCandidates.Generated("card", "wide", "lg"), ["card", "wide", "lg"]),
            (ClassJoinCandidates.Generated("card", "wide", "lg", "is-open"),
                ["card", "wide", "lg", "is-open"]),
            (ClassJoinCandidates.Generated("card", null, "lg", null), ["card", null, "lg", null]),
        ];

        foreach (var (generatedJoin, shape) in joinShapes)
        {
            string? runtimeJoin = ClassJoinCandidates.Runtime(shape);
            if (generatedJoin != runtimeJoin)
            {
                Console.Error.WriteLine(
                    $"The class join candidates disagree on [{string.Join(", ", shape)}]: the " +
                    $"generated class answers '{generatedJoin ?? "null"}' and the runtime method " +
                    $"'{runtimeJoin ?? "null"}', so a comparison between them would not measure " +
                    "where the join belongs (#239).");
                return 1;
            }
        }

        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(ResolveArtifactsPath());

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;

        static int FrameCount(Action<RenderTreeBuilder> build)
        {
            var builder = new RenderTreeBuilder();
            build(builder);
            return builder.GetFrames().Count;
        }

        // Shared tail for the three §7.1 frame-equivalence gates above: each compares a different pair
        // against a different DESIGN.md claim, named by its own message, so the gates stay individually
        // attributable while the compare-report-fail mechanics live in one place.
        static bool FramesDiffer(RenderTreeBuilder expected, RenderTreeBuilder actual, string message)
        {
            var differences = FrameEquivalence.Compare(expected, actual);
            if (differences.Count == 0)
            {
                return false;
            }

            Console.Error.WriteLine(message);
            foreach (string difference in differences)
            {
                Console.Error.WriteLine($"  {difference}");
            }

            return true;
        }
    }

    /// <summary>
    /// Returns <c>artifacts/benchmarks</c> under the repository root.
    /// </summary>
    /// <remarks>
    /// BenchmarkDotNet otherwise writes to <c>BenchmarkDotNet.Artifacts/</c> beside the executable,
    /// which .gitignore does not cover, whereas <c>artifacts/</c> already is ignored. The root is
    /// found by walking up for the solution file rather than by counting <c>..</c> segments, so a
    /// change to the output layout cannot silently redirect the output somewhere tracked.
    /// </remarks>
    private static string ResolveArtifactsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BlazorCodeFirst.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate BlazorCodeFirst.slnx above " + AppContext.BaseDirectory +
                ", so the benchmark artifacts path cannot be resolved to the ignored artifacts/ directory.");
        }

        return Path.Combine(directory.FullName, "artifacts", "benchmarks");
    }
}

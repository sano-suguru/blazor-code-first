using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BlazorCodeFirst.Benchmarks.Components;
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

        var differences = FrameEquivalence.Compare(generated, razor);
        if (differences.Count > 0)
        {
            Console.Error.WriteLine(
                "BenchmarkView and BenchmarkViewRazor do not render equivalent frames, so an " +
                "allocation comparison between them would not measure what DESIGN.md §7.1 claims:");
            foreach (string difference in differences)
            {
                Console.Error.WriteLine($"  {difference}");
            }

            return 1;
        }

        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(ResolveArtifactsPath());

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;
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

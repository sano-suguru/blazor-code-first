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

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}

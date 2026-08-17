using BenchmarkDotNet.Attributes;
using BlazorCodeFirst.Benchmarks.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.Benchmarks;

/// <summary>
/// The measurement behind DESIGN.md §7.1: allocations per render, BlazorCodeFirst against an
/// equivalent Razor component, at two boundaries.
/// </summary>
/// <remarks>
/// <c>Program.Main</c> has already proved the two components render equivalent frames before any of
/// these run, so a difference in allocated bytes is attributable to the compilation strategy.
/// </remarks>
[MemoryDiagnoser]
public class AllocationBenchmarks
{
    private readonly BenchmarkView _generated = new();
    private readonly BenchmarkViewRazor _razor = new();
    private readonly RenderTreeBuilder _builder = new();

    private BatchRenderer _generatedRenderer = null!;
    private BatchRenderer _razorRenderer = null!;
    private BenchmarkView _generatedRoot = null!;
    private BenchmarkViewRazor _razorRoot = null!;

    /// <summary>
    /// Attaches a root component to each renderer and performs the one initial render.
    /// </summary>
    /// <remarks>
    /// The initial render happens here rather than inside a benchmark because
    /// <c>Renderer.RenderRootComponentAsync</c> may be called only once per root component id; calling
    /// it twice throws. The render-cycle benchmarks therefore drive re-renders through
    /// <c>Invalidate</c>, which is also the more useful figure: it includes the diff against an
    /// existing tree, which is what an application pays on every state change.
    /// </remarks>
    [GlobalSetup]
    public void Setup()
    {
        _generatedRenderer = new BatchRenderer();
        _razorRenderer = new BatchRenderer();
        _generatedRoot = new BenchmarkView();
        _razorRoot = new BenchmarkViewRazor();

        _generatedRenderer.RenderOnce(_generatedRoot);
        _razorRenderer.RenderOnce(_razorRoot);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _generatedRenderer.Dispose();
        _razorRenderer.Dispose();
        _builder.Dispose();
    }

    /// <summary>Boundary one, generated: what the source generator's output allocates.</summary>
    [Benchmark(Baseline = true, Description = "BuildRenderTree: BlazorCodeFirst")]
    public void BuildRenderTree_Generated()
    {
        _builder.Clear();
        _generated.Build(_builder);
    }

    /// <summary>Boundary one, Razor: what the Razor compiler's output allocates.</summary>
    [Benchmark(Description = "BuildRenderTree: Razor")]
    public void BuildRenderTree_Razor()
    {
        _builder.Clear();
        _razor.Build(_builder);
    }

    /// <summary>Boundary two, generated: one re-render including the diff.</summary>
    [Benchmark(Description = "Render cycle: BlazorCodeFirst")]
    public Task RenderCycle_Generated() =>
        _generatedRenderer.Dispatcher.InvokeAsync(_generatedRoot.Invalidate);

    /// <summary>Boundary two, Razor: the same cycle for the Razor component.</summary>
    [Benchmark(Description = "Render cycle: Razor")]
    public Task RenderCycle_Razor() =>
        _razorRenderer.Dispatcher.InvokeAsync(_razorRoot.Invalidate);
}

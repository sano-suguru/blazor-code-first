using BenchmarkDotNet.Attributes;
using BlazorCodeFirst.Benchmarks.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.Benchmarks;

/// <summary>
/// The measurement behind DESIGN.md §7.1's remaining dynamic-content-path sentence: whether the Opaque
/// path (§5.3) costs anything beyond a hand-written
/// <see cref="Microsoft.AspNetCore.Components.RenderFragment"/>, at the same two boundaries
/// <see cref="AllocationBenchmarks"/> uses for the SSC comparison.
/// </summary>
/// <remarks>
/// <c>Program.Main</c> has already proved <see cref="OpaqueContentView"/> and
/// <see cref="OpaqueContentViewRazor"/> render equivalent frames before any of these run, so a
/// difference in allocated bytes is attributable to the <c>View</c>/<c>ViewRuntime.FragmentOf</c>
/// indirection rather than to a structural mismatch.
/// </remarks>
[MemoryDiagnoser]
public class OpaqueContentBenchmarks
{
    private readonly OpaqueContentView _generated = new();
    private readonly OpaqueContentViewRazor _razor = new();
    private readonly RenderTreeBuilder _builder = new();

    private BatchRenderer _generatedRenderer = null!;
    private BatchRenderer _razorRenderer = null!;
    private OpaqueContentView _generatedRoot = null!;
    private OpaqueContentViewRazor _razorRoot = null!;

    /// <summary>Attaches a root component to each renderer and performs the one initial render.</summary>
    /// <remarks>See the remarks on <see cref="AllocationBenchmarks.Setup"/>; this mirrors it.</remarks>
    [GlobalSetup]
    public void Setup()
    {
        _generatedRenderer = new BatchRenderer();
        _razorRenderer = new BatchRenderer();
        _generatedRoot = new OpaqueContentView();
        _razorRoot = new OpaqueContentViewRazor();

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

    /// <summary>Boundary one, generated: what the generated Opaque emission allocates.</summary>
    [Benchmark(Baseline = true, Description = "BuildRenderTree: BlazorCodeFirst (Opaque)")]
    public void BuildRenderTree_Generated()
    {
        _builder.Clear();
        _generated.Build(_builder);
    }

    /// <summary>Boundary one, Razor: what a directly hand-written <c>RenderFragment</c> call allocates.</summary>
    [Benchmark(Description = "BuildRenderTree: hand-written RenderFragment")]
    public void BuildRenderTree_Razor()
    {
        _builder.Clear();
        _razor.Build(_builder);
    }

    /// <summary>Boundary two, generated: one re-render including the diff.</summary>
    [Benchmark(Description = "Render cycle: BlazorCodeFirst (Opaque)")]
    public Task RenderCycle_Generated() =>
        _generatedRenderer.Dispatcher.InvokeAsync(_generatedRoot.Invalidate);

    /// <summary>Boundary two, Razor: the same cycle for the hand-written-fragment component.</summary>
    [Benchmark(Description = "Render cycle: hand-written RenderFragment")]
    public Task RenderCycle_Razor() =>
        _razorRenderer.Dispatcher.InvokeAsync(_razorRoot.Invalidate);
}

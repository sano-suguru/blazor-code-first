using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BlazorCodeFirst.IntegrationTests.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.Benchmarks;

/// <summary>
/// The measurement behind the #140 decision: what folding a contiguous static run into one
/// <c>AddMarkupContent</c> frame would buy.
/// </summary>
/// <remarks>
/// <strong>These figures are a decision input and are not DESIGN.md §7.1 published values.</strong>
/// §7.1 declines to publish times because the variance is large and machine-dependent, and it
/// records allocations only. A time figure from this class must not reach the whitepaper. This is
/// deliberately a separate class from <see cref="AllocationBenchmarks"/>, which is the §7.1
/// measurement, so that the two are not read as one set.
/// <para>
/// Both sides of each pair are BlazorCodeFirst components: the markup spelling uses <c>Html.Raw</c>,
/// which emits exactly one markup frame, to reproduce the folded frame shape with no compiler
/// change. Keeping Razor out of the comparison isolates the fold difference from the other two
/// differences #140 found — Razor's interpolation splitting and its inter-tag whitespace frames —
/// both of which are artifacts of Razor's source format rather than gaps on this side.
/// </para>
/// <para>
/// <c>Program.Main</c> has already proved each pair emits strictly fewer folded frames, and
/// <c>FoldFixtureTests</c> has proved each pair renders the same DOM, before any of these run.
/// </para>
/// <para>
/// The judgment criterion is the <c>MannWhitney(10%)</c> column: folding is implemented only if the
/// static-heavy render cycle clears 10%. The threshold was fixed before any number existed, and the
/// column exists so that it appears in the output rather than in someone's reading of Mean ± Error.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[StatisticalTestColumn("10%")]
public class StaticFoldBenchmarks
{
    private readonly StaticHeavyElementView _staticHeavyElement = new();
    private readonly StaticHeavyMarkupView _staticHeavyMarkup = new();
    private readonly MixedElementView _mixedElement = new();
    private readonly MixedMarkupView _mixedMarkup = new();
    private readonly RenderTreeBuilder _builder = new();

    private BatchRenderer _staticHeavyElementRenderer = null!;
    private BatchRenderer _staticHeavyMarkupRenderer = null!;
    private BatchRenderer _mixedElementRenderer = null!;
    private BatchRenderer _mixedMarkupRenderer = null!;

    private StaticHeavyElementView _staticHeavyElementRoot = null!;
    private StaticHeavyMarkupView _staticHeavyMarkupRoot = null!;
    private MixedElementView _mixedElementRoot = null!;
    private MixedMarkupView _mixedMarkupRoot = null!;

    /// <summary>
    /// Attaches a root component to each of the four renderers and performs the one initial render,
    /// for the reason <see cref="AllocationBenchmarks.Setup"/> gives: a root component id may be
    /// rendered through <c>RenderRootComponentAsync</c> only once, so the cycle benchmarks drive
    /// re-renders through <c>Invalidate</c>.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _staticHeavyElementRenderer = new BatchRenderer();
        _staticHeavyMarkupRenderer = new BatchRenderer();
        _mixedElementRenderer = new BatchRenderer();
        _mixedMarkupRenderer = new BatchRenderer();

        _staticHeavyElementRoot = new StaticHeavyElementView();
        _staticHeavyMarkupRoot = new StaticHeavyMarkupView();
        _mixedElementRoot = new MixedElementView();
        _mixedMarkupRoot = new MixedMarkupView();

        RenderOnce(_staticHeavyElementRenderer, _staticHeavyElementRoot);
        RenderOnce(_staticHeavyMarkupRenderer, _staticHeavyMarkupRoot);
        RenderOnce(_mixedElementRenderer, _mixedElementRoot);
        RenderOnce(_mixedMarkupRenderer, _mixedMarkupRoot);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _staticHeavyElementRenderer.Dispose();
        _staticHeavyMarkupRenderer.Dispose();
        _mixedElementRenderer.Dispose();
        _mixedMarkupRenderer.Dispose();
        _builder.Dispose();
    }

    [Benchmark(Baseline = true, Description = "BuildRenderTree: element frames")]
    [BenchmarkCategory("StaticHeavy/Build")]
    public void StaticHeavy_Build_Element()
    {
        _builder.Clear();
        _staticHeavyElement.Build(_builder);
    }

    [Benchmark(Description = "BuildRenderTree: folded markup")]
    [BenchmarkCategory("StaticHeavy/Build")]
    public void StaticHeavy_Build_Markup()
    {
        _builder.Clear();
        _staticHeavyMarkup.Build(_builder);
    }

    [Benchmark(Baseline = true, Description = "Render cycle: element frames")]
    [BenchmarkCategory("StaticHeavy/Cycle")]
    public Task StaticHeavy_Cycle_Element() =>
        _staticHeavyElementRenderer.Dispatcher.InvokeAsync(_staticHeavyElementRoot.Invalidate);

    [Benchmark(Description = "Render cycle: folded markup")]
    [BenchmarkCategory("StaticHeavy/Cycle")]
    public Task StaticHeavy_Cycle_Markup() =>
        _staticHeavyMarkupRenderer.Dispatcher.InvokeAsync(_staticHeavyMarkupRoot.Invalidate);

    [Benchmark(Baseline = true, Description = "BuildRenderTree: element frames")]
    [BenchmarkCategory("LowStatic/Build")]
    public void Mixed_Build_Element()
    {
        _builder.Clear();
        _mixedElement.Build(_builder);
    }

    [Benchmark(Description = "BuildRenderTree: folded markup")]
    [BenchmarkCategory("LowStatic/Build")]
    public void Mixed_Build_Markup()
    {
        _builder.Clear();
        _mixedMarkup.Build(_builder);
    }

    [Benchmark(Baseline = true, Description = "Render cycle: element frames")]
    [BenchmarkCategory("LowStatic/Cycle")]
    public Task Mixed_Cycle_Element() =>
        _mixedElementRenderer.Dispatcher.InvokeAsync(_mixedElementRoot.Invalidate);

    [Benchmark(Description = "Render cycle: folded markup")]
    [BenchmarkCategory("LowStatic/Cycle")]
    public Task Mixed_Cycle_Markup() =>
        _mixedMarkupRenderer.Dispatcher.InvokeAsync(_mixedMarkupRoot.Invalidate);

    private static void RenderOnce(BatchRenderer renderer, IComponent root)
    {
        int id = renderer.Attach(root);
        renderer.Dispatcher.InvokeAsync(() => renderer.RenderAsync(id)).GetAwaiter().GetResult();
    }
}

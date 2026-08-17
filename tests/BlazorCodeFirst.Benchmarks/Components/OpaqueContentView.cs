using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Benchmarks.Components;

/// <summary>
/// The BlazorCodeFirst side of the §7.1 dynamic-content comparison: SSC content plus one child built
/// from a hand-written <see cref="RenderFragment"/>, the genuine Opaque case (ARCHITECTURE.md §2.3,
/// §3.2).
/// </summary>
/// <remarks>
/// <see cref="DynamicContent"/> carries no <c>[ViewPart]</c> and does not reference the design-time
/// surface, so the generator cannot analyse it: it emits
/// <c>AddContent(seq, ViewRuntime.FragmentOf(DynamicContent()))</c> and reports BCF2001 (see
/// <c>OpaqueCallDiagnosticTests</c>). <see cref="OpaqueContentViewRazor"/> reaches the same
/// <c>RenderTreeBuilder.AddContent(seq, RenderFragment)</c> call directly, so the two sides measure
/// whether the <c>View</c> indirection costs anything beyond the fragment's own allocation.
/// </remarks>
public partial class OpaqueContentView : BodyComponentBase
{
    public string Title { get; set; } = "Benchmark";

    public int Count { get; set; }

    protected override View Body =>
        Div.Class("card")[
            H1[Title],
            DynamicContent()];

    private View DynamicContent()
    {
        RenderFragment fragment = builder => builder.AddContent(0, $"Count: {Count}");
        return fragment;
    }

    /// <summary>Exposes the generated render path to the benchmark and the equivalence gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    /// <summary>Counterpart to <see cref="BenchmarkView.Invalidate"/>; see the remarks there.</summary>
    public void Invalidate() => StateHasChanged();
}

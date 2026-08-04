using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The low-static shape of the #140 fold measurement, written the way the surface is written today.
/// </summary>
/// <remarks>
/// The three dynamic slots are interleaved so that each static element stands alone between dynamic
/// siblings and no foldable run is longer than one element. This is the lower bound of the range:
/// the payoff from folding scales with the static fraction, so one shape would give a point where
/// two give a range.
/// <para>
/// Plain properties rather than <c>[Parameter]</c>s, for the reason
/// <see cref="StaticHeavyElementView.Index"/> gives.
/// </para>
/// </remarks>
public partial class MixedElementView : BodyComponentBase
{
    public int Count { get; set; }

    public int Sum { get; set; }

    public int Tax { get; set; }

    protected override View Body =>
        Div.Class("row")[
            H2["Totals"],
            Span[$"Items: {Count}"],
            P["Subtotal before tax"],
            Span[$"Sum: {Sum}"],
            Span[$"Tax: {Tax}"]];

    /// <summary>Exposes the generated render path to the benchmark and the frame gates.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    /// <summary>
    /// Requests a re-render. The render-cycle benchmark needs this because
    /// <c>Renderer.RenderRootComponentAsync</c> may be called only once per root component id.
    /// </summary>
    public void Invalidate() => StateHasChanged();
}

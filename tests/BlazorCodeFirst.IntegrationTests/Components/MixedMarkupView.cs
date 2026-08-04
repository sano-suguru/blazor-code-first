using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The low-static shape with its two foldable runs folded into one <c>AddMarkupContent</c> frame
/// each, which is the output the generator would produce if #140 were implemented.
/// </summary>
/// <remarks>
/// Each run is a single element here, so this fold saves one frame per run rather than the seventeen
/// the static-heavy shape saves. That contrast is the point of measuring two shapes.
/// </remarks>
public partial class MixedMarkupView : BodyComponentBase
{
    public int Count { get; set; }

    public int Sum { get; set; }

    public int Tax { get; set; }

    protected override View Body =>
        Div.Class("row")[
            Raw("<h2>Totals</h2>"),
            Span[$"Items: {Count}"],
            Raw("<p>Subtotal before tax</p>"),
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

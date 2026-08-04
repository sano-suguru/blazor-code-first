using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The static-heavy shape of the #140 fold measurement, written the way the surface is written today:
/// every static node becomes an element frame plus a text frame.
/// </summary>
/// <remarks>
/// Page chrome and copy with exactly one dynamic slot, which is the case #140 argues folding wins.
/// One slot, not zero and not several: zero would collapse the whole <c>Body</c> into a single
/// markup frame and measure a 100%-folding case no real component reaches, and several would
/// fragment the static runs and stop the shape from being static-heavy.
/// <para>
/// The text is literal throughout, unlike <c>BenchmarkView</c>, which drives every text node from a
/// property so that its frames match the Razor side of the §7.1 comparison. A property reference is
/// not a compile-time constant, so nothing in <c>BenchmarkView</c> would be foldable at all and it
/// cannot serve as an element-spelling side here.
/// </para>
/// </remarks>
public partial class StaticHeavyElementView : BodyComponentBase
{
    /// <summary>
    /// The one dynamic value, splitting the static children into two runs.
    /// </summary>
    /// <remarks>
    /// A plain property rather than a <c>[Parameter]</c>, for the reason
    /// <c>BannerListComponent.ShowBanner</c> gives: the frame gates read frames from a component that
    /// was never attached to a renderer, and BL0005 forbids setting a component parameter from
    /// outside. Nothing needs a non-default value here — the frame counts do not depend on it, and
    /// what the measurement needs from this property is only that the span's content is an
    /// interpolation rather than a constant, which is true at any value.
    /// </remarks>
    public int Index { get; set; }

    protected override View Body =>
        Div.Class("doc")[
            H1["BlazorCodeFirst"],
            Nav.Class("toc")[
                A.Href("#design")["Design"],
                A.Href("#architecture")["Architecture"],
                A.Href("#contributing")["Contributing"]],
            P["The element surface is one unified node carrying a string tag."],
            Span[$"Section {Index}"],
            P["Attributes are written before children."],
            P["No second vocabulary is stacked on top of HTML."]];

    /// <summary>Exposes the generated render path to the benchmark and the frame gates.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    /// <summary>
    /// Requests a re-render. The render-cycle benchmark needs this because
    /// <c>Renderer.RenderRootComponentAsync</c> may be called only once per root component id.
    /// </summary>
    public void Invalidate() => StateHasChanged();
}

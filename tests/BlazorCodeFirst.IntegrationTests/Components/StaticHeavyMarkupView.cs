using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The static-heavy shape with every contiguous static run folded into one <c>AddMarkupContent</c>
/// frame, which is the output the generator would produce if #140 were implemented.
/// </summary>
/// <remarks>
/// <c>Html.Raw</c> emits exactly one markup frame with <c>Width</c> = 1, so this reproduces the
/// folded frame shape with no compiler change. It does not reproduce the folded <em>generated
/// code</em>; the measurement rests on the premise that runtime cost is determined by the frame
/// shape, which is a premise and not a finding.
/// <para>
/// The wrapper <c>div</c> stays an element frame because a markup frame carries complete markup, so
/// an open tag cannot be folded together with a partial child list. Razor keeps the element frame
/// here for the same reason. The two runs are split by the dynamic slot, exactly as Razor splits
/// them.
/// </para>
/// <para>
/// The markup strings carry no whitespace between tags, matching what the element spelling emits.
/// Whitespace would make the stand-in unfaithful: a longer string to parse on the browser side, and
/// text nodes the element spelling does not have.
/// </para>
/// </remarks>
public partial class StaticHeavyMarkupView : BodyComponentBase
{
    /// <summary>
    /// The one dynamic value, splitting the static children into two runs. A plain property for the
    /// reason <see cref="StaticHeavyElementView.Index"/> gives.
    /// </summary>
    public int Index { get; set; }

    protected override View Body =>
        Div.Class("doc")[
            Raw("""<h1>BlazorCodeFirst</h1><nav class="toc"><a href="#design">Design</a><a href="#architecture">Architecture</a><a href="#contributing">Contributing</a></nav><p>The element surface is one unified node carrying a string tag.</p>"""),
            Span[$"Section {Index}"],
            Raw("""<p>Attributes are written before children.</p><p>No second vocabulary is stacked on top of HTML.</p>""")];

    /// <summary>Exposes the generated render path to the benchmark and the frame gates.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    /// <summary>
    /// Requests a re-render. The render-cycle benchmark needs this because
    /// <c>Renderer.RenderRootComponentAsync</c> may be called only once per root component id.
    /// </summary>
    public void Invalidate() => StateHasChanged();
}

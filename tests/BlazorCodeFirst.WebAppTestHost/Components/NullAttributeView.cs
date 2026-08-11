using BlazorCodeFirst;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components;

/// <summary>
/// What a null attribute value does in a real browser (#171). The .NET side is covered elsewhere — frames
/// by <c>NullAttributeFrameTests</c>, rendering and the diff by the integration tests, prerendered HTML by
/// <c>PrerenderTests</c> — and none of them can see the one thing that only exists in a browser: whether
/// the client-side renderer applies the <c>RemoveAttribute</c> edit the diff asks for, and whether it
/// patches the element already in the DOM rather than replacing it.
/// </summary>
/// <remarks>
/// Every id below is read by <c>null-attribute.spec.ts</c>. Renaming one drops a browser assertion
/// silently, because a selector that matches nothing only fails where something asserts on it.
/// <para>
/// Routed by <c>NullAttributePage.razor</c> so that page can disable prerendering, for the reason recorded
/// on <see cref="FoldParityView"/>: with prerendering on, the first HTML response already carries the final
/// attributes, and a first render batch identical to that DOM produces no edits at all, so the
/// browser-side renderer never runs and the comparison would be of server-written HTML.
/// </para>
/// </remarks>
public partial class NullAttributeView : BodyComponentBase
{
    private bool _present = true;

    protected override View Body =>
        Fragment(
            Span.Attr("id", "to-null").Attr("title", _present ? "tip" : null)["to-null"],
            Span.Attr("id", "to-empty").Attr("title", _present ? "tip" : "")["to-empty"],
            Span.Attr("id", "class-join").Class("card").Class(_present ? "active" : null)["class-join"],
            Span.Attr("id", "class-single").Class(_present ? "active" : null)["class-single"],
            Span.Attr("id", "class-leading").Class(_present ? "card" : null).Class("active")["class-leading"],
            Span.Attr("id", "class-both").Class(_present ? "card" : null).Class(_present ? "active" : null)["class-both"],
            Span.Attr("id", "bool").Attr("data-v", _present)["bool"],
            Button.Attr("id", "toggle").OnClick(() => _present = !_present)["toggle"],
            Span.Attr("id", "state")[_present ? "present" : "absent"]);

    /// <summary>Exposes the generated render path to <c>NullAttributeFrameTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    /// <summary>
    /// Flips the state <see cref="Body"/> reads. The premise the browser comparison rests on is a frame
    /// count in the <em>absent</em> state, and the toggle's click path is not reachable from a bare
    /// <see cref="RenderTreeBuilder"/>.
    /// </summary>
    internal void Toggle() => _present = !_present;
}

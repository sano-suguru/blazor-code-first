using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components;

/// <summary>
/// Hosts the one shape that can observe <c>SetUpdatesAttributeName</c>: a text input whose setter
/// normalizes the incoming value. Blazor's DOM resynchronization repairs a divergence between what the
/// user typed and what the render tree holds, and a setter that rewrites the value is the only thing
/// that creates one — a plain <c>_name = v</c> binding never diverges.
/// </summary>
/// <remarks>
/// No .NET-side test can see this. bUnit's <c>Input()</c> writes the value that reaches the setter
/// straight into the AngleSharp DOM — bUnit's markup is a projection of the render tree, not a live DOM
/// someone typed into — so the divergence never exists there; that was measured, after a first attempt
/// claimed otherwise and a mutation of the emission left every bUnit test green. Prerendering writes one
/// static snapshot and dispatches no events at all. The mechanism needs a real browser: the JS renderer
/// sends the DOM's current value back with the event (Blazor's <c>EventFieldInfo</c>), and the
/// server-side diff for the attribute named here compares the new value against <em>that</em> rather
/// than against the previous render tree value. Without it, a re-render that produces the value the
/// render tree already held emits no edit, and the element keeps displaying the un-normalized text.
/// <para>
/// So <c>bind-resync.spec.ts</c> in <c>BlazorCodeFirst.WebAppTests/browser</c> is the only cover this
/// emission's runtime effect has anywhere. <c>dotnet test</c> does not reach it, but the
/// <c>browser</c> job in <c>.github/workflows/ci.yml</c> runs it on every pull request.
/// <c>BindResyncTests</c> in <c>WebAppTests</c> pins the .NET-side premise the browser test depends on,
/// the same way <c>FoldParityTests</c> does for <c>fold-parity.spec.ts</c>.
/// </para>
/// </remarks>
[Route("/bind-resync")]
public partial class BindResyncView : BodyComponentBase
{
    protected override View Body =>
        Fragment(
            Component<TrimmingInputProbe>(),

            // Playwright waits for this before typing, so the interaction goes through the live circuit
            // and not the prerendered snapshot, which dispatches no events and so can never resync.
            If(RendererInfo.IsInteractive, () => Span.Attr("id", "bind-resync-ready")["ready"]));
}

/// <summary>The binding itself, kept in its own component so <see cref="Build"/> can be called without a
/// render handle (<c>RendererInfo</c> on <see cref="BindResyncView"/> needs one).</summary>
public partial class TrimmingInputProbe : BodyComponentBase
{
    private string _name = "";

    private int _writes;

    protected override View Body =>
        Div.Class("bind-resync")[
            Input.Attr("id", "trimmed-input").Type("text")
                .Bind("value", "oninput", () => _name, Normalize),

            // The browser test's settle signal. The scenario's whole point is that the input's value in
            // the render tree does *not* change on the second keystroke, so nothing about the input can
            // report that the round trip finished; this counter can, because it moves on every call.
            Span.Attr("id", "write-count")[$"{_writes}"],

            // The normalized field, so a failure says whether the field or only the DOM went wrong.
            Span.Attr("id", "field-value")[_name]];

    /// <summary>Exposes the generated render path to the premise gate in <c>BindResyncTests</c>.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    private void Normalize(string value)
    {
        _writes++;
        _name = value.Trim();
    }
}

/// <summary>
/// The other door to the same repair: a value the framework's converter cannot parse. #307 opened it by
/// letting a non-<see cref="string"/> type be bound at all — every string parses, so before it no
/// element-side binding could reject its input.
/// </summary>
/// <remarks>
/// The rejection differs from <see cref="TrimmingInputProbe"/>'s normalization in the one way that
/// matters here: the setter is never called, so the field does not move at all and the re-render
/// produces exactly what the previous one did. Nothing in the render tree changes, and the repair is the
/// only thing that can put the element back. Microsoft documents the outcome — "the unparsable value is
/// automatically reverted to its previous value when the bind event is triggered" — and this measures
/// that this surface reaches it.
/// <para>
/// <c>oninput</c> rather than <c>onchange</c>, which is also what makes the trap visible: the reversion
/// runs on every keystroke, so an <see langword="int"/> bound this way cannot accept a typed <c>.</c> at
/// all. The site documentation says so, and this page is where that is measured rather than asserted.
/// </para>
/// </remarks>
[Route("/bind-reject")]
public partial class BindRejectView : BodyComponentBase
{
    protected override View Body =>
        Fragment(
            Component<RejectingInputProbe>(),
            If(RendererInfo.IsInteractive, () => Span.Attr("id", "bind-reject-ready")["ready"]));
}

/// <summary>The rejecting binding, in its own component for the reason <see cref="TrimmingInputProbe"/>
/// is: <see cref="Build"/> needs no render handle.</summary>
public partial class RejectingInputProbe : BodyComponentBase
{
    private int _age = 30;

    protected override View Body =>
        Div.Class("bind-reject")[
            // type="text" rather than "number": a number input refuses to hold an unparsable string at
            // all, so the browser would swallow the divergence before Blazor ever saw it.
            Input.Attr("id", "rejecting-input").Type("text")
                .Bind("value", "oninput", () => _age, System.Globalization.CultureInfo.InvariantCulture),

            // No settle counter, unlike TrimmingInputProbe. Nothing here can move on a rejected value —
            // that is the scenario — so the browser test establishes the live circuit with a value that
            // is accepted, and then relies on Playwright's own retry for the reversion.
            Span.Attr("id", "reject-field-value")[$"{_age}"]];

    /// <summary>Exposes the generated render path to the premise gate in <c>BindResyncTests</c>.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// The same normalizing input as <see cref="TrimmingInputProbe"/>, with a second binding beside it.
/// BCF3021's withdrawn justification predicted that the second binding's SetUpdatesAttributeName would
/// overwrite the first and cost it its repair; the storage is per frame, so it does not (#162).
/// </summary>
[Route("/bind-resync-two")]
public partial class TwoBindingResyncView : BodyComponentBase
{
    protected override View Body =>
        Fragment(
            Component<TwoBindingProbe>(),
            If(RendererInfo.IsInteractive, () => Span.Attr("id", "two-binding-ready")["ready"]));
}

/// <summary>The two bindings, in their own component for the same reason <see cref="TrimmingInputProbe"/>
/// is: <see cref="Build"/> needs no render handle.</summary>
public partial class TwoBindingProbe : BodyComponentBase
{
    private string _name = "";

    private string _committed = "";

    private int _writes;

    protected override View Body =>
        Div.Class("bind-resync")[
            Input.Attr("id", "two-bound-input").Type("text")
                .Bind("value", "oninput", () => _name, Normalize)
                .Bind("data-committed", "onchange", () => _committed),

            Span.Attr("id", "two-write-count")[$"{_writes}"],
            Span.Attr("id", "two-field-value")[_name]];

    /// <summary>Exposes the generated render path to the premise gate in <c>BindResyncTests</c>.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    private void Normalize(string value)
    {
        _writes++;
        _name = value.Trim();
    }
}

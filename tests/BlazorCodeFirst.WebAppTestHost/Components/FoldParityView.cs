using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components;

/// <summary>
/// Hosts six shapes that need a real browser to verify #140's static fold: each one
/// renders the same content twice, once through a fully-static, folded spelling and once through an
/// otherwise-identical spelling routed through a non-constant property so the generator's #140 fold
/// cannot apply. The folded spelling reaches the DOM by assigning HTML text to a shared
/// <c>&lt;template&gt;</c>'s <c>innerHTML</c> (mirroring <c>blazor.web.js</c>'s <c>insertMarkup</c>); the
/// unfolded spelling reaches it through <c>createElement</c>/<c>setAttribute</c>/<c>createTextNode</c>.
/// No .NET-side test can tell these apart: bUnit parses a document string with AngleSharp, and
/// prerendering writes markup verbatim through the .NET <c>HtmlRenderer</c>. Only a real browser's HTML
/// parser can show whether the two paths disagree, which is why this page exists and
/// <c>fold-parity.spec.ts</c> is the only place in the repository that drives one.
/// </summary>
/// <remarks>
/// The comparison this page enables is worthless unless the folded container really did fold and the
/// unfolded container really did not: if either side's premise silently flips, the two containers would
/// still render identical DOM (because the browser is not being asked to detect a defect, just to prove
/// two equivalent-looking inputs equal each other) while testing nothing. <c>FoldParityTests</c> in
/// <c>WebAppTests</c> pins the frame counts behind every probe below directly, so that failure mode is
/// caught before the browser is ever asked to look.
/// </remarks>
/// <remarks>
/// Routed by <c>FoldParityPage.razor</c> rather than carrying <c>[Route]</c> itself, specifically so that
/// page can disable prerendering. With prerendering on (the host's ambient default), the browser's first
/// HTML for this route would already contain every container fully populated — the .NET
/// <c>HtmlRenderer</c> writes markup verbatim during that pass — and if the interactive circuit's first
/// render batch is identical to what is already in the DOM, Blazor emits no edits and the JS renderer
/// (<c>insertMarkup</c>/<c>insertText</c>) never runs at all. The comparison would then be between two
/// subtrees that both came from the server-written HTML, which is exactly the path
/// <c>PrerenderTests</c> already covers, not the one this file exists to check.
/// </remarks>
public partial class FoldParityView : BodyComponentBase
{
    protected override View Body =>
        Fragment(
            Component<TableFragmentProbe>(),
            Component<SelectOptionsProbe>(),
            Component<EscapedTextProbe>(),
            Component<QuotedAttributeProbe>(),
            Component<VoidTagInRunProbe>(),
            Component<MultiClassProbe>(),

            // Playwright waits for this before comparing DOM, so the comparison is of the live,
            // hydrated render and not of the prerendered markup that the .NET HtmlRenderer already
            // writes verbatim (and which PrerenderTests already covers).
            If(RendererInfo.IsInteractive, () => Span.Attr("id", "interactive-marker")["ready"]));
}

/// <summary>
/// Shape 1: a run of <c>td</c> siblings inside a <c>tr</c>, wrapped in a
/// <c>table</c>/<c>tbody</c> so both spellings enter valid table-related HTML insertion modes. This
/// matters specifically because <c>&lt;template&gt;</c> parsing starts in the "in template" insertion
/// mode, which recognizes <c>table</c>/<c>tbody</c>/<c>tr</c>/<c>td</c> and switches into the matching
/// table sub-mode; a stray <c>tr</c>/<c>td</c> reached only through the ordinary "in body" mode is a
/// parse error and the token is dropped instead of inserted. No other test in this repository parses
/// folded table markup as HTML.
/// </summary>
public partial class TableFragmentProbe : BodyComponentBase
{
    private static string CellA => "a";

    private static string CellB => "b";

    protected override View Body =>
        Fragment(
            Table.Attr("id", "folded-table-fragment")[
                Tbody[Tr[Td["a"], Td["b"]]]],
            Table.Attr("id", "unfolded-table-fragment")[
                Tbody[Tr[Td[CellA], Td[CellB]]]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 2: a run of <c>option</c> siblings inside a <c>select</c>. Like the table
/// shape, <c>select</c> as the very first tag in template content correctly switches the parser into "in
/// select" mode; wrapping it in anything else first would risk the same drop that motivates
/// <see cref="TableFragmentProbe"/>.
/// </summary>
public partial class SelectOptionsProbe : BodyComponentBase
{
    private static string OptionA => "a";

    private static string OptionB => "b";

    protected override View Body =>
        Fragment(
            Select.Attr("id", "folded-select-options")[
                Option["a"], Option["b"]],
            Select.Attr("id", "unfolded-select-options")[
                Option[OptionA], Option[OptionB]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 3: static text carrying characters HTML must escape, plus payloads that
/// probe the escaping is real rather than merely tolerated: a raw <c>&lt;/script&gt;</c>, a raw HTML
/// comment, and an <c>img</c>-with-<c>onerror</c> string that must stay text rather than parse as an
/// element. <see cref="StaticMarkupSerializer"/>'s unit tests check the serializer's output string; this
/// is the only place that checks a real browser parses that string back into the same DOM as the element
/// path.
/// </summary>
public partial class EscapedTextProbe : BodyComponentBase
{
    private static string AmpText => "a & b < c > d";

    private static string ScriptCloseText => "</script>";

    private static string CommentText => "<!-- x -->";

    private static string ImgPayloadText => "<img src=x onerror=alert(1)>";

    protected override View Body =>
        Fragment(
            Div.Attr("id", "folded-escaped-text")[
                P["a & b < c > d"],
                P["</script>"],
                P["<!-- x -->"],
                P["<img src=x onerror=alert(1)>"]],
            Div.Attr("id", "unfolded-escaped-text")[
                P[AmpText],
                P[ScriptCloseText],
                P[CommentText],
                P[ImgPayloadText]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 4: a static attribute value containing a double quote, plus a
/// classic attribute-breakout payload (<c>" onmouseover="alert(1)</c>) that must stay a single quoted
/// attribute value rather than close the attribute and open a new one.
/// </summary>
public partial class QuotedAttributeProbe : BodyComponentBase
{
    private static string QuoteValue => "say \"hi\"";

    private static string BreakoutValue => "\" onmouseover=\"alert(1)";

    protected override View Body =>
        Fragment(
            Div.Attr("id", "folded-quoted-attribute")[
                Span.Attr("data-value", "say \"hi\"")["a"],
                Span.Attr("data-value", "\" onmouseover=\"alert(1)")["b"]],
            Div.Attr("id", "unfolded-quoted-attribute")[
                Span.Attr("data-value", QuoteValue)["a"],
                Span.Attr("data-value", BreakoutValue)["b"]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 5: a void element (<c>img</c>) followed by an ordinary sibling inside a
/// folded run. No fixture in the snapshot corpus contains this shape, so <see cref="StaticMarkupSerializer"/>'s
/// unit tests are the only other check that a void tag inside a fold is written without a closing tag or
/// children; this is the only check that a browser parses the result back to the same DOM as
/// <c>createElement("img")</c> with no children.
/// </summary>
/// <remarks>
/// Both of <c>Img</c>'s attributes are routed through properties on the unfolded side, not just the
/// <c>Span</c>'s text. A literal <c>Img.Src("pixel.gif").Alt("px")</c> is itself a complete, self-contained
/// foldable node (open tag plus two attributes, no children), so if only the span's text were made
/// non-constant, the generator would still fold the img alone into its own <c>AddMarkupContent</c> frame
/// sitting next to ordinary element frames — and Blazor's InteractiveServer rendering then inserts an
/// internal <c>&lt;!--!--&gt;</c> boundary comment around that embedded markup frame, a real DOM node the
/// fully-folded side never gets. (That marker is not specific to prerendering: it appears in the live,
/// post-hydration DOM the same way, since this whole page never prerenders any content — see
/// <c>FoldParityPage.razor</c>.) That is a construction artifact of a partially-folded probe, not a #140
/// defect, and it was caught here by comparing against the actual rendered DOM rather than assuming the
/// span's non-constant text was enough to keep the whole container unfolded. <c>FoldParityTests</c> pins
/// this container's unfolded frame count exactly (not merely "not one frame") for the same reason: this
/// defect shrank that count without ever un-rooting the container as an <c>Element</c> frame, so only an
/// exact match would have caught it at the .NET layer instead of the browser.
/// </remarks>
public partial class VoidTagInRunProbe : BodyComponentBase
{
    private static string PixelSrc => "pixel.gif";

    private static string PixelAlt => "px";

    private static string VoidRunText => "x";

    protected override View Body =>
        Fragment(
            Div.Attr("id", "folded-void-in-run")[
                Img.Src("pixel.gif").Alt("px"),
                Span["x"]],
            Div.Attr("id", "unfolded-void-in-run")[
                Img.Src(PixelSrc).Alt(PixelAlt),
                Span[VoidRunText]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 6: an element decorated with three <c>.Class(...)</c> calls, which ARCHITECTURE.md §2.7
/// documents as collapsing into one <c>class</c> attribute joined by a single space. No fixture in the snapshot corpus
/// contains this shape either; the attribute is on a child of the id-carrying container specifically so
/// comparing the container's <c>innerHTML</c> in the browser test captures the child's own serialized
/// <c>class</c> attribute rather than the container's.
/// </summary>
public partial class MultiClassProbe : BodyComponentBase
{
    private static string WideClass => "wide";

    protected override View Body =>
        Fragment(
            Div.Attr("id", "folded-multi-class")[
                Span.Class("btn").Class("btn-primary").Class("wide")["click"]],
            Div.Attr("id", "unfolded-multi-class")[
                Span.Class("btn").Class("btn-primary").Class(WideClass)["click"]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

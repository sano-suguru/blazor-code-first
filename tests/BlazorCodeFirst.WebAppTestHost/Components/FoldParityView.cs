using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components;

/// <summary>
/// Hosts twelve shapes that need a real browser to verify #140's static fold. The first six each
/// render the same content twice, once through a fully-static, folded spelling and once through an
/// otherwise-identical spelling routed through a non-constant property so the generator's #140 fold
/// cannot apply, and so do the last two (<see cref="SweptCharactersProbe"/>,
/// <see cref="BooleanAttributeProbe"/>). The other four
/// (<see cref="CarriageReturnProbe"/>, <see cref="NullCharacterProbe"/>,
/// <see cref="LoneSurrogateProbe"/>, <see cref="LeadingByteOrderMarkProbe"/>) are the exception: each pins a value <c>CanRoundTrip</c>
/// <em>refuses</em> and measures what the two paths actually do with it, so the refusal rests on a
/// measurement rather than on a reading of the parsing specification. The folded spelling reaches
/// the DOM by assigning HTML text to a shared
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
            Component<CarriageReturnProbe>(),
            Component<NullCharacterProbe>(),
            Component<LoneSurrogateProbe>(),
            Component<LeadingByteOrderMarkProbe>(),
            Component<SweptCharactersProbe>(),
            Component<BooleanAttributeProbe>(),

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

/// <summary>
/// Shape 7: a carriage return (U+000D), which <see cref="StaticMarkupSerializer"/>'s
/// <c>CanRoundTrip</c> refuses to fold. This is the only shape here that exists to pin a
/// <em>refusal</em>, so it carries three containers rather than the usual two: the static spelling that
/// must not fold, and the markup and element paths spelled explicitly so the divergence the refusal
/// avoids is itself measured rather than assumed.
/// </summary>
/// <remarks>
/// The HTML parser normalizes CRLF and a lone CR to LF during input-stream preprocessing, before
/// tokenization, and that applies to fragment parsing on a <c>&lt;template&gt;</c> as much as to a
/// document. So a folded CR would reach the DOM as LF while <c>setAttribute</c>/<c>createTextNode</c>
/// keep it. Attribute values are not whitespace-collapsed, which makes <c>getAttribute</c> the sharpest
/// witness. Measured in Chromium here, not derived from the specification: the markup container returns
/// <c>"a\nb"</c> where the element container returns <c>"a\rb"</c>.
/// <para>
/// This matters more than the NUL and lone-surrogate cases <c>CanRoundTrip</c> also refuses, because a
/// CR is reachable from ordinary source — any verbatim string literal in a file checked out with CRLF
/// line endings carries one — where those two require someone to write an escape deliberately.
/// </para>
/// <para>
/// The markup path is reached through <c>Html.Raw</c> rather than by folding, for the reason the whole
/// probe exists: after the fix, nothing here folds. <c>Raw</c> emits exactly one markup frame, so it
/// puts a CR through the identical browser-side <c>insertMarkup</c> path a fold would have used, which is
/// what makes the measurement still hold once the refusal is in place. <c>FoldParityTests</c> pins both
/// premises: that the static container emits no markup frame, and that the <c>Raw</c> container emits
/// exactly one.
/// </para>
/// <para>
/// Standing in <c>Raw</c> for a fold rests on one premise, stated here rather than left implicit:
/// <c>Raw</c> passes its argument to <c>AddMarkupContent</c> verbatim, performing no normalization or
/// sanitization of its own. Were it ever to gain any, this container would stop reproducing what a fold
/// produces, and the browser comparison would go red — or worse, agree — for a reason with nothing to do
/// with folding.
/// </para>
/// </remarks>
public partial class CarriageReturnProbe : BodyComponentBase
{
    private static string CrAttributeValue => "a\rb";

    private static string CrText => "a\rb";

    protected override View Body =>
        Fragment(
            // Spelled entirely from constants, so this would fold if CanRoundTrip admitted a CR. It must
            // not, and it must therefore render identically to the element container below.
            Div.Attr("id", "refused-carriage-return")[
                Span.Attr("data-value", "a\rb")["one"],
                P["a\rb"]],

            // The markup path, entered deliberately: this is what folding would have produced.
            Div.Attr("id", "markup-carriage-return")[
                Raw("<span data-value=\"a\rb\">one</span><p>a\rb</p>")],

            // The element path, through non-constant values so no part of it can fold.
            Div.Attr("id", "element-carriage-return")[
                Span.Attr("data-value", CrAttributeValue)["one"],
                P[CrText]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 8: a NUL (U+0000), the second value <c>CanRoundTrip</c> refuses. Three containers, for the
/// same reason <see cref="CarriageReturnProbe"/> carries three.
/// </summary>
/// <remarks>
/// This probe exists because the recorded reason for the refusal was wrong in both halves, and nothing
/// measured it. <c>CanRoundTrip</c>'s remarks said the parser "maps both a literal NUL and the
/// reference <c>&amp;#0;</c> to U+FFFD". The reference half cannot happen at all — the serializer
/// escapes <c>&amp;</c> to <c>&amp;amp;</c>, so no character reference can ever form out of a value —
/// and the literal half is only half true. Measured in Chromium here, the markup path replaces a NUL
/// with U+FFFD in an <em>attribute value</em> but drops it entirely from <em>text content</em>, where
/// <c>createTextNode</c> keeps it. So the divergence is real and the refusal stands, but it takes two
/// different shapes and neither was the one written down.
/// <para>
/// Both shapes are asserted rather than just their disagreement with the element path, because they
/// are what a future browser change would move. The refusal itself is the third container.
/// </para>
/// </remarks>
public partial class NullCharacterProbe : BodyComponentBase
{
    private static string NulAttributeValue => "a\0b";

    private static string NulText => "a\0b";

    protected override View Body =>
        Fragment(
            // Constant throughout, so this folds unless CanRoundTrip refuses the NUL.
            Div.Attr("id", "refused-null")[
                Span.Attr("data-value", "a\0b")["one"],
                P["a\0b"]],

            // What folding would have produced, entered through Raw for the reason CarriageReturnProbe
            // gives: after the refusal there is nothing left that folds.
            Div.Attr("id", "markup-null")[
                Raw("<span data-value=\"a\0b\">one</span><p>a\0b</p>")],

            Div.Attr("id", "element-null")[
                Span.Attr("data-value", NulAttributeValue)["one"],
                P[NulText]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 9: a lone surrogate (an unpaired U+D800), the third value <c>CanRoundTrip</c> refuses.
/// </summary>
/// <remarks>
/// Unlike the other refusals here, this one has no divergence behind it, and the measurement is what
/// established that. The parsing specification makes a lone surrogate in the input stream a
/// <c>surrogate-in-input-stream</c> parse error and nothing more — parse errors do not rewrite the
/// stream — so the claim that it was refused "for the same reason" as a NUL was never right. Measured
/// here, it does not even reach the parser: all three containers read back <c>"a\uFFFDb"</c>, because
/// .NET's UTF-8 encoding of the render batch replaces the surrogate before either path leaves the
/// server, and both paths take that same trip.
/// <para>
/// The refusal is kept. It costs a missed fold on a value no author writes by accident, and dropping it
/// would change behaviour on the strength of one hosting model — this host renders interactive-server,
/// and nothing here has measured WebAssembly. What this probe buys is that the agreement is now under
/// measurement rather than assumed: if a future runtime delivered the surrogate intact on one path
/// only, the refusal would acquire a real justification and this probe is what would say so.
/// </para>
/// </remarks>
public partial class LoneSurrogateProbe : BodyComponentBase
{
    private static string SurrogateAttributeValue => "a\ud800b";

    private static string SurrogateText => "a\ud800b";

    protected override View Body =>
        Fragment(
            Div.Attr("id", "refused-lone-surrogate")[
                Span.Attr("data-value", "a\ud800b")["one"],
                P["a\ud800b"]],

            Div.Attr("id", "markup-lone-surrogate")[
                Raw("<span data-value=\"a\ud800b\">one</span><p>a\ud800b</p>")],

            Div.Attr("id", "element-lone-surrogate")[
                Span.Attr("data-value", SurrogateAttributeValue)["one"],
                P[SurrogateText]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 10: a value that <em>begins</em> with U+FEFF, which <c>CanRoundTrip</c> refuses. The refusal
/// looks like the other three but rests on a different mechanism, and #150's sweep found it by
/// measurement after reading the parsing specification had ruled the character safe.
/// </summary>
/// <remarks>
/// No HTML parsing stage touches a BOM: it is stripped by the byte-stream decode, which fragment
/// parsing on a <c>&lt;template&gt;</c> never runs. The stripping happens one layer earlier and once
/// per frame string, when the browser decodes the render batch — a UTF-8 decode drops a byte order
/// mark in first position and keeps it anywhere else. That makes the divergence positional rather
/// than character-based, and folding is exactly what moves the position: unfolded, a text or
/// attribute value is its own frame string and a leading BOM is its first character, so it is
/// stripped; folded, the same value sits inside a larger markup string that opens with
/// <c>&lt;</c>, so it survives.
/// <para>
/// Measured in Chromium, on all three containers below. It is the one refusal here where the markup
/// path is the <em>more</em> faithful of the two — the fold would preserve a character the element
/// path loses — but the fold's contract is that both spellings produce the same DOM, not that either
/// is the better reading of the source.
/// </para>
/// <para>
/// The third container is what identifies the mechanism rather than merely the symptom, and is why
/// this probe carries one more container than <see cref="CarriageReturnProbe"/>. Its <c>Raw</c>
/// string starts with the BOM instead of with a tag, so the markup path loses it too. Without that
/// container the measurement would equally support "the element path drops a leading BOM", which is
/// the wrong rule and would have produced the wrong predicate.
/// </para>
/// </remarks>
public partial class LeadingByteOrderMarkProbe : BodyComponentBase
{
    private static string LeadingBomText => "\uFEFFab";

    private static string LeadingBomAttribute => "\uFEFFab";

    protected override View Body =>
        Fragment(
            // Constant throughout, so this folds unless CanRoundTrip refuses a leading BOM.
            Div.Attr("id", "refused-leading-bom")[
                Span.Attr("data-value", "\uFEFFab")["one"],
                P["\uFEFFab"]],

            // What folding would have produced: the value sits in the interior of the markup string,
            // because the string opens with the span's tag.
            Div.Attr("id", "markup-leading-bom")[
                Raw("<span data-value=\"\uFEFFab\">one</span><p>\uFEFFab</p>")],

            Div.Attr("id", "element-leading-bom")[
                Span.Attr("data-value", LeadingBomAttribute)["one"],
                P[LeadingBomText]],

            // The same markup path, but with the BOM in first position of the frame string rather than
            // inside it. This is the container that names the mechanism: position, not path.
            Div.Attr("id", "markup-leading-bom-at-string-start")[
                Raw("\uFEFFab<span>one</span>")]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 11: one representative of every character class the parsing specification singles out and
/// <c>CanRoundTrip</c> nonetheless admits. This is the standing form of #150's sweep: the sweep's
/// finding was that the fold predicate has no hole, and a finding of "nothing here diverges" is worth
/// nothing unless something keeps checking it.
/// </summary>
/// <remarks>
/// Input-stream preprocessing rewrites exactly one thing, newlines, which is
/// <see cref="CarriageReturnProbe"/>'s subject. Everything else the same section names — surrogates,
/// noncharacters, and controls other than ASCII whitespace and NUL — it classifies as a parse error
/// only, which does not rewrite the stream. NUL is handled later, in tokenization and tree
/// construction, and is <see cref="NullCharacterProbe"/>'s subject. So the classes below are the ones
/// the specification calls out and then leaves alone, and each is here because "the specification says
/// it is left alone" is exactly the kind of claim this repository does not accept without a browser.
/// <para>
/// The two containers must agree with each other <em>and</em> with the source, and
/// <c>fold-parity.spec.ts</c> asserts both. Agreement alone would be satisfied by a defect that
/// mangled the two paths identically.
/// </para>
/// </remarks>
public partial class SweptCharactersProbe : BodyComponentBase
{
    // Spelled with \u escapes throughout. Every character here is invisible, and several are
    // noncharacters that an editor, a diff viewer or a formatter is entitled to mangle; an escape
    // survives all three and can be read. The folded side repeats each literal instead of naming the
    // property, because a literal is what the fold needs to see -- that duplication is the premise of
    // the comparison, and a typo in it fails the comparison rather than passing silently.

    /// <summary>A C0 control and DEL, both <c>control-character-in-input-stream</c> parse errors.</summary>
    private static string ControlsText => "a\u0001b\u007Fc";

    /// <summary>NEL, NBSP, and the Unicode line and paragraph separators.</summary>
    private static string SeparatorsText => "a\u0085b\u00A0c\u2028d\u2029e";

    /// <summary>An interior BOM, and a U+FFFD that was already one in the source.</summary>
    private static string ByteOrderMarkText => "a\uFEFFb\uFFFDc";

    /// <summary>BMP noncharacters and an astral one, all <c>noncharacter-in-input-stream</c> parse errors.</summary>
    private static string NoncharactersText => "a\uFFFEb\uFFFFc\uFDD0d\uD83F\uDFFEe";

    /// <summary>Tab, line feed, a doubled space, and a correctly paired astral character.</summary>
    private static string WhitespaceText => "a\tb\nc  d\uD83D\uDE00e";

    /// <summary>Several of the classes at once, in attribute position rather than text position.</summary>
    private static string MixedAttributeValue => "a\u0001b\u0085c\uFEFFd\uFFFFe\tf  g";

    protected override View Body =>
        Fragment(
            Div.Attr("id", "folded-swept-characters")[
                Span.Attr("data-value", "a\u0001b\u0085c\uFEFFd\uFFFFe\tf  g")["one"],
                P["a\u0001b\u007Fc"],
                P["a\u0085b\u00A0c\u2028d\u2029e"],
                P["a\uFEFFb\uFFFDc"],
                P["a\uFFFEb\uFFFFc\uFDD0d\uD83F\uDFFEe"],
                P["a\tb\nc  d\uD83D\uDE00e"]],
            Div.Attr("id", "unfolded-swept-characters")[
                Span.Attr("data-value", MixedAttributeValue)["one"],
                P[ControlsText],
                P[SeparatorsText],
                P[ByteOrderMarkText],
                P[NoncharactersText],
                P[WhitespaceText]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

/// <summary>
/// Shape 12: an attribute whose value is a <c>bool</c> (#158). The one non-string value the fold admits,
/// and the only shape here whose two DOM outcomes are produced by two different mechanisms: the folded
/// side writes <c>disabled=""</c> and writes nothing at all for the false one, while the element side
/// hands the <c>bool</c> to <c>AddAttribute</c> and lets Blazor's conditional-attribute behaviour decide.
/// </summary>
/// <remarks>
/// The equivalence was measured with a hand-written <c>RenderTreeBuilder</c> component before the
/// surface could express it (#158): <c>AddAttribute</c> with <c>true</c> reaches the DOM as
/// <c>name=""</c>, indistinguishable from an empty string value, and with <c>false</c> the attribute is
/// absent. Both halves are what the fold now relies on, and nothing kept that measurement true, which is
/// what this probe is for.
/// <para>
/// A false value is the sharper half. It is the one case where the fold writes <em>nothing</em> for an
/// attribute the source spells out, so a defect there is invisible in the folded markup and shows up
/// only against the element path. The two paths do not even agree on frame count — the element path
/// emits no frame for a false <c>bool</c> either, which is what <c>FoldParityTests</c> pins.
/// </para>
/// </remarks>
public partial class BooleanAttributeProbe : BodyComponentBase
{
    private static bool True => true;

    private static bool False => false;

    protected override View Body =>
        Fragment(
            Div.Attr("id", "folded-boolean-attribute")[
                Input.Attr("disabled", true).Attr("hidden", false),
                Span.Attr("data-flag", true)["one"]],
            Div.Attr("id", "unfolded-boolean-attribute")[
                Input.Attr("disabled", True).Attr("hidden", False),
                Span.Attr("data-flag", True)["one"]]);

    /// <summary>Exposes the generated render path to <c>FoldParityTests</c>' premise gate.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

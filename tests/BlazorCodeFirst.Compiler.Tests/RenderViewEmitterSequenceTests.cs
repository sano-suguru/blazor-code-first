using System.Collections.Generic;
using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Checks the emitter's sequence allocation as a property of the emitted text, over every node kind and
/// the combinations where one node's reservation has to leave room for another's.
/// </summary>
public sealed class RenderViewEmitterSequenceTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderViewEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", TypeParameters: default, Namespace: null,
            RootNode: root)).ToString();

    private static ExpressionTemplate Code(string code) => ExpressionTemplate.Literal(code);

    private static ElementNode Element(string tag, params RenderNode[] children) =>
        new(tag, default, default, default, ImmutableArray.Create(children));

    private static ElementNode Span(string literal) =>
        Element("span", new TextContentNode(Code(literal)));

    public static TheoryData<string> CaseNames() => new([.. Cases.Keys]);

    /// <summary>
    /// Every <see cref="RenderNode"/> kind appears at least once, and every construct that reserves a
    /// range for something other than itself (an <c>If</c>'s two branches, a <c>ForEach</c>'s content, a
    /// component's slots) appears with a following sibling, which is the position a reservation error
    /// moves.
    /// </summary>
    private static readonly Dictionary<string, RenderNode> Cases =
        new(StringComparer.Ordinal)
        {
            // --- elements ------------------------------------------------------------------------
            ["element-bare"] = Element("div"),
            ["element-text-child"] = Span("\"a\""),
            ["element-class-attributes-events-children"] = new ElementNode(
                "a",
                ImmutableArray.Create(Code("\"nav\""), Code("\"wide\"")),
                ImmutableArray.Create(
                    new AttributeTemplate("href", Code("\"/a\"")),
                    new AttributeTemplate("id", Code("\"x\""))),
                ImmutableArray.Create(new EventTemplate("onclick", Code("() => { }"))),
                ImmutableArray.Create<RenderNode>(
                    new TextContentNode(Code("\"Home\"")),
                    Span("\"!\""))),
            ["element-nested-three-deep"] = Element("div", Element("p", Span("\"deep\""))),

            // --- text, fragment, raw, external fragment ------------------------------------------
            ["text-content-root"] = new TextContentNode(Code("\"bare\"")),
            ["fragment-empty"] = new FragmentNode(ImmutableArray<RenderNode>.Empty),
            ["fragment-mixed-children"] = new FragmentNode(ImmutableArray.Create<RenderNode>(
                Span("\"a\""),
                new RawMarkupNode(Code("\"<i>y</i>\"")),
                Span("\"b\""))),
            ["fragment-inside-element-with-sibling"] = Element(
                "div",
                new FragmentNode(ImmutableArray.Create<RenderNode>(Span("\"a\""), Span("\"b\""))),
                Span("\"after\"")),
            ["raw-markup-root"] = new RawMarkupNode(Code("\"<b>x</b>\"")),
            ["render-fragment-content-with-sibling"] = Element(
                "div",
                new RenderFragmentContentNode(Code("Body")),
                Span("\"after\"")),

            // --- If: the reservation covers a range that may not execute -------------------------
            ["if-without-else-then-sibling"] = Element(
                "div",
                new IfNode(Code("_on"), Span("\"Yes\""), null),
                Span("\"Always\"")),
            ["if-with-else-then-sibling"] = Element(
                "div",
                new IfNode(Code("_on"), Span("\"Yes\""), Span("\"No\"")),
                Span("\"Always\"")),
            ["if-nested-in-then-branch"] = Element(
                "div",
                new IfNode(
                    Code("_outer"),
                    new IfNode(Code("_inner"), Span("\"in\""), Span("\"out\"")),
                    Span("\"else\"")),
                Span("\"Always\"")),

            // --- ForEach: content template reserved once, re-emitted per iteration ---------------
            ["foreach-then-sibling"] = Element(
                "ul",
                new ForEachNode(
                    Code("_items"), Code("item"),
                    Element("li", new TextContentNode(Code("item"))), "item"),
                Span("\"after\"")),
            ["foreach-content-nested-element"] = Element(
                "ul",
                new ForEachNode(Code("_items"), Code("item"), Element("li", Span("item")), "item"),
                Span("\"after\"")),

            // --- components: slots continue the flat counter --------------------------------------
            ["component-parameters-only-then-sibling"] = Element(
                "div",
                new ComponentNode(
                    "global::T.Card",
                    ImmutableArray.Create(
                        new ComponentParameter("Title", Code("\"t\"")),
                        new ComponentParameter("Count", Code("1")))),
                Span("\"after\"")),
            ["component-two-slots-then-sibling"] = Element(
                "div",
                new ComponentNode(
                    "global::T.Card",
                    ImmutableArray.Create(new ComponentParameter("Title", Code("\"t\""))),
                    ImmutableArray.Create(
                        new ComponentSlotNode("ChildContent", new TextContentNode(Code("\"x\""))),
                        new ComponentSlotNode("Footer", Span("\"f\"")))),
                Span("\"after\"")),
            ["component-slot-containing-foreach"] = Element(
                "div",
                new ComponentNode(
                    "global::T.Card",
                    default,
                    ImmutableArray.Create(new ComponentSlotNode(
                        "ChildContent",
                        Element("ul", new ForEachNode(
                            Code("_items"), Code("item"),
                            Element("li", new TextContentNode(Code("item"))), "item"))))),
                Span("\"after\"")),

            // --- Composable expansion: locals consume no sequence ---------------------------------
            ["expansion-with-locals"] = new ExpansionNode(
                ImmutableArray.Create(new LocalBinding("string", "__c0", Code("\"heading\""))),
                Element("div", Span("__c0"), Span("\"b\""))),
        };

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EmittedSequenceArguments_AreDense(string caseName) =>
        SequenceArguments.AssertDense(EmitRoot(Cases[caseName]));
}

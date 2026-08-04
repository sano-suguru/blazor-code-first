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

    // -----------------------------------------------------------------------------------------------
    // The three facts below were recorded only by SequenceAllocatorTests, which asserted them about an
    // independently computed width rather than about emitted code. They are restated here as assertions
    // on the sequence numbers that actually ship (#69).
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void EmitComponent_SlotCostsOneParameterFramePlusItsContent_SoTheNextSiblingClearsThem()
    {
        // A slot costs one AddComponentParameter frame plus the whole width of its content, and a slot's
        // frames continue the enclosing flat counter instead of restarting (ARCHITECTURE.md §2.7). The
        // component below occupies 1..7 — OpenComponent 1, Title 2, ChildContent 3 + its text 4, Footer 5
        // + its span 6 + that span's text 7 — so the following sibling must land on 8.
        var node = Element(
            "div",
            new ComponentNode(
                "global::T.Card",
                ImmutableArray.Create(new ComponentParameter("Title", Code("\"t\""))),
                ImmutableArray.Create(
                    new ComponentSlotNode("ChildContent", new TextContentNode(Code("\"x\""))),
                    new ComponentSlotNode("Footer", Span("\"f\"")))),
            Span("\"after\""));

        var code = EmitRoot(node);

        Assert.Contains("__builder.OpenComponent<global::T.Card>(1);", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddComponentParameter(2, \"Title\", \"t\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddComponentParameter(3, \"ChildContent\", ", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(4, \"x\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddComponentParameter(5, \"Footer\", ", code, StringComparison.Ordinal);
        Assert.Contains("__builder.OpenElement(6, \"span\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(7, \"f\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.OpenElement(8, \"span\");", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitIf_BranchRangesAreDisjoint_AndTheNextSiblingClearsBothBranches()
    {
        // Both branches are reserved whichever one runs, so the else range starts where the then range
        // ends and the following sibling starts past both. A sibling at 4 would mean the else branch had
        // reused the then branch's numbers, and toggling the condition would remount the sibling.
        var node = Element(
            "div",
            new IfNode(Code("_on"), Span("\"Yes\""), Span("\"No\"")),
            Span("\"Always\""));

        var code = EmitRoot(node);

        Assert.Contains("__builder.OpenRegion(1);", code, StringComparison.Ordinal);
        Assert.Contains("__builder.OpenElement(2, \"span\");", code, StringComparison.Ordinal);   // then
        Assert.Contains("__builder.AddContent(3, \"Yes\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.OpenElement(4, \"span\");", code, StringComparison.Ordinal);   // else
        Assert.Contains("__builder.AddContent(5, \"No\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.OpenElement(6, \"span\");", code, StringComparison.Ordinal);   // sibling
        Assert.Contains("__builder.AddContent(7, \"Always\");", code, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitClassAttribute_FoldsAnyNumberOfClassesIntoOneFrame_SoTheNextSiblingIsUnmoved()
    {
        // ARCHITECTURE.md §2.7(A): repeated .Class decorations collapse into a single class attribute
        // frame, so a decorated element's frame width does not depend on how many classes it carries.
        var one = EmitWithClasses(Code("\"x\""));
        var three = EmitWithClasses(Code("\"x\""), Code("\"y\""), Code("\"z\""));

        // One class attribute frame at 2 in both spellings, and the following sibling unmoved at 4.
        Assert.Contains("__builder.AddAttribute(2, \"class\", \"x\");", one, StringComparison.Ordinal);
        Assert.Contains(
            "__builder.AddAttribute(2, \"class\", (\"x\") + \" \" + (\"y\") + \" \" + (\"z\"));",
            three,
            StringComparison.Ordinal);
        Assert.Contains("__builder.OpenElement(4, \"span\");", one, StringComparison.Ordinal);
        Assert.Contains("__builder.OpenElement(4, \"span\");", three, StringComparison.Ordinal);
        Assert.Equal(SequenceArguments.InTextOrder(one).Count, SequenceArguments.InTextOrder(three).Count);
    }

    /// <summary>A decorated span carrying <paramref name="classes"/>, followed by an undecorated sibling.</summary>
    private static string EmitWithClasses(params ExpressionTemplate[] classes) =>
        EmitRoot(Element(
            "div",
            new ElementNode(
                "span",
                ImmutableArray.Create(classes),
                default,
                default,
                ImmutableArray.Create<RenderNode>(new TextContentNode(Code("\"a\"")))),
            Span("\"after\"")));
}

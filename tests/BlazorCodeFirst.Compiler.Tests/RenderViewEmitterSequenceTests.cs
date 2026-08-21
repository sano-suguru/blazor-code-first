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

    /// <summary>
    /// A non-constant expression. Every case below that uses this stays unfolded, which is deliberate:
    /// these baselines were recorded against the element form, and keeping them unfolded preserves that
    /// evidence. The folded shape is covered by the <c>folded-*</c> cases, which use <see cref="Const"/>.
    /// </summary>
    private static ExpressionTemplate Code(string code) => ExpressionTemplate.Literal(code);

    /// <summary>An expression carrying a constant string value, so nodes built from it fold.</summary>
    private static ExpressionTemplate Const(string value) =>
        ExpressionTemplate.Create(
            [new LiteralExpressionSegment(
                Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true))],
            new StringConstant(value));

    private static ElementNode Element(string tag, params RenderNode[] children) =>
        new(tag, default, default, default, ImmutableArray.Create(children));

    private static ElementNode Span(string literal) =>
        Element("span", new TextContentNode(Code(literal)));

    private static ElementNode StaticSpan(string text) =>
        Element("span", new TextContentNode(Const(text)));

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
                    Element("li", new TextContentNode(Code("item"))), null, "item"),
                Span("\"after\"")),
            ["foreach-content-nested-element"] = Element(
                "ul",
                new ForEachNode(Code("_items"), Code("item"), Element("li", Span("item")), null, "item"),
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
                            Element("li", new TextContentNode(Code("item"))), null, "item"))))),
                Span("\"after\"")),
            ["component-mixed-scalar-and-slot-kinds"] = new ComponentNode(
                "global::T.Card",
                ImmutableArray.Create(new ComponentParameter("Title", Code("\"t\""))),
                ImmutableArray.Create(
                    new ComponentSlotNode("ChildContent", new TextContentNode(Code("\"plain\""))),
                    new ComponentSlotNode("Ignored", new TextContentNode(Code("\"ignored\"")))
                    {
                        Kind = ComponentSlotKind.GenericContextIgnored,
                        ContextTypeName = "global::System.Int32",
                    },
                    new ComponentSlotNode("Contextual", new TextContentNode(Code("__bcf_context_6")))
                    {
                        Kind = ComponentSlotKind.GenericContextual,
                        ContextTypeName = "global::System.String",
                        ContextVariableName = "__bcf_context_6",
                    })),

            // --- ViewPart expansion: locals consume no sequence ---------------------------------
            ["expansion-with-locals"] = new ExpansionNode(
                ImmutableArray.Create(new LocalBinding("string", "__c0", Code("\"heading\""))),
                Element("div", Span("__c0"), Span("\"b\""))),

            // --- folded runs ----------------------------------------------------------------------
            // A markup frame reserves and writes exactly one sequence number, so the density property
            // holds without a special case. These cases put a fold next to every construct that reserves
            // a range for something else, which is where a reservation error would move.
            ["folded-run-in-element"] = Element("div", StaticSpan("a"), StaticSpan("b")),
            ["folded-run-split-by-dynamic-sibling"] = Element(
                "div",
                StaticSpan("a"),
                Element("span", new TextContentNode(Code("Count"))),
                StaticSpan("b")),
            ["folded-fragment-root"] = new FragmentNode(
                ImmutableArray.Create<RenderNode>(StaticSpan("a"), StaticSpan("b"))),
            ["folded-both-if-branches-with-sibling"] = Element(
                "div",
                new IfNode(Code("_flag"), Element("p", StaticSpan("t")), Element("p", StaticSpan("f"))),
                StaticSpan("after")),
            ["folded-inside-foreach-content"] = Element(
                "div",
                new ForEachNode(Code("_items"), Code("item.Id"), Element("li", StaticSpan("x")), null, "item"),
                StaticSpan("after")),
            ["folded-component-slot-with-sibling"] = Element(
                "div",
                new ComponentNode(
                    "global::T.Card",
                    ImmutableArray.Create(new ComponentParameter("Title", Code("\"t\""))),
                    ImmutableArray.Create(new ComponentSlotNode("ChildContent", Element("p", StaticSpan("a"))))),
                StaticSpan("after")),

            // --- non-attribute frame decorations (§2.7(E)) -----------------------------------------
            // Of the three, only a reference capture reserves a number, and it reserves it between the
            // element's attributes and its children. Each case puts a following sibling after the
            // decorated node, which is where a capture that reserved without writing (or wrote without
            // reserving) would show.
            ["element-ref-then-sibling"] = Element(
                "div",
                new ElementNode(
                    "input",
                    default,
                    ImmutableArray.Create(new AttributeTemplate("type", Code("\"text\""))),
                    default,
                    default)
                {
                    Ref = Code("r => _input = r"),
                },
                Span("\"after\"")),

            // A key takes no number, so the child after it keeps the number it would have had. The
            // sibling is what makes that observable rather than merely asserted.
            ["element-key-and-ref-then-sibling"] = Element(
                "div",
                new ElementNode("li", default, default, default, ImmutableArray.Create<RenderNode>(Span("\"x\"")))
                {
                    Key = Code("item.Id"),
                    Ref = Code("r => _row = r"),
                },
                Span("\"after\"")),

            ["component-render-mode-and-ref-then-sibling"] = Element(
                "div",
                new ComponentNode(
                    "global::T.Card",
                    ImmutableArray.Create(new ComponentParameter("Title", Code("\"t\""))),
                    ImmutableArray.Create(new ComponentSlotNode("ChildContent", Element("p", Span("\"a\"")))))
                {
                    Key = Code("_id"),
                    RenderMode = Code("_mode"),
                    Ref = Code("c => _card = c"),
                },
                Span("\"after\"")),
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
    public void EmitComponent_MixedSlotKinds_ContinueOneDenseFlatSequence()
    {
        var code = EmitRoot(Cases["component-mixed-scalar-and-slot-kinds"]);

        Assert.Contains("__builder.OpenComponent<global::T.Card>(0);", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddComponentParameter(1, \"Title\", \"t\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddComponentParameter(2, \"ChildContent\", ", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(3, \"plain\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddComponentParameter(4, \"Ignored\", ", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(5, \"ignored\");", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddComponentParameter(6, \"Contextual\", ", code, StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(7, __bcf_context_6);", code, StringComparison.Ordinal);
        SequenceArguments.AssertDense(code);
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
            "__builder.AddAttribute(2, \"class\", __BlazorCodeFirstJoinClasses((\"x\"), (\"y\"), (\"z\")));",
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

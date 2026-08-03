using System.Collections.Immutable;
using System.Linq;
using BlazorCodeFirst.Compiler;
using BlazorCodeFirst.Compiler.Generation;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class SequenceAllocatorTests
{
    private static ElementNode Span(ExpressionTemplate content, EquatableArray<ExpressionTemplate> classes = default) =>
        new("span", classes, default, default, ImmutableArray.Create<RenderNode>(new TextContentNode(content)));

    private static ElementNode Button(
        ExpressionTemplate label,
        ExpressionTemplate handler,
        EquatableArray<ExpressionTemplate> classes = default) =>
        new(
            "button",
            classes,
            default,
            ImmutableArray.Create(new EventTemplate("onclick", handler)),
            ImmutableArray.Create<RenderNode>(new TextContentNode(label)));

    private static ElementNode Div(
        EquatableArray<RenderNode> children, EquatableArray<ExpressionTemplate> classes = default) =>
        new("div", classes, default, default, children);

    private static ElementNode DivWithAttrs(
        EquatableArray<AttributeTemplate> attributes,
        EquatableArray<ExpressionTemplate> classes = default) =>
        new("div", classes, attributes, default, ImmutableArray<RenderNode>.Empty);

    [Fact]
    public void SequenceAllocator_SpanElement_HasWidthTwo()
    {
        Assert.Equal(2, SequenceAllocator.Width(Span(ExpressionTemplate.Literal("\"hello\""))));
    }

    [Fact]
    public void SequenceAllocator_ButtonElement_HasWidthThree()
    {
        Assert.Equal(3, SequenceAllocator.Width(Button(
            ExpressionTemplate.Literal("\"label\""),
            ExpressionTemplate.Literal("() => { }"))));
    }

    [Fact]
    public void SequenceAllocator_DivElement_HasWidthOfOnePlusChildWidths()
    {
        var children = ImmutableArray.Create<RenderNode>(
            Span(ExpressionTemplate.Literal("\"a\"")),
            Button(
                ExpressionTemplate.Literal("\"b\""),
                ExpressionTemplate.Literal("() => { }")));

        Assert.Equal(
            1 + children.Sum(SequenceAllocator.Width),
            SequenceAllocator.Width(Div(children)));
    }

    [Fact]
    public void SequenceAllocator_EmptyDiv_HasWidthOne()
    {
        Assert.Equal(1, SequenceAllocator.Width(Div(ImmutableArray<RenderNode>.Empty)));
    }

    // -----------------------------------------------------------------------
    // IfNode
    // -----------------------------------------------------------------------

    [Fact]
    public void SequenceAllocator_IfNodeWithoutElse_HasWidthOfOnePlusThenBranch()
    {
        // OpenRegion(k) + then contents, else branch absent
        var then = Span(ExpressionTemplate.Literal("\"yes\""));
        Assert.Equal(
            1 + SequenceAllocator.Width(then),
            SequenceAllocator.Width(new IfNode(ExpressionTemplate.Literal("_visible"), then, null)));
    }

    [Fact]
    public void SequenceAllocator_IfNodeWithElse_HasWidthOfOnePlusBothBranches()
    {
        // OpenRegion(k) + then contents + else contents
        var then = Span(ExpressionTemplate.Literal("\"yes\""));
        var otherwise = Button(
            ExpressionTemplate.Literal("\"no\""),
            ExpressionTemplate.Literal("() => { }"));
        var ifNode = new IfNode(ExpressionTemplate.Literal("_visible"), then, otherwise);
        Assert.Equal(
            1 + SequenceAllocator.Width(then) + SequenceAllocator.Width(otherwise),
            SequenceAllocator.Width(ifNode));
    }

    [Fact]
    public void SequenceAllocator_IfNodeBranches_UseDisjointRanges()
    {
        // then  range: [k+1, k+1+W(then))
        // else  range: [k+1+W(then), k+1+W(then)+W(else))
        // Total width accounts for both so the next node always starts at k+Width(if),
        // regardless of which branch executed.
        var then = Span(ExpressionTemplate.Literal("\"yes\""));
        var otherwise = Span(ExpressionTemplate.Literal("\"no\""));
        int thenW = SequenceAllocator.Width(then);
        int elseW = SequenceAllocator.Width(otherwise);
        Assert.Equal(
            1 + thenW + elseW,
            SequenceAllocator.Width(new IfNode(ExpressionTemplate.Literal("_v"), then, otherwise)));
    }

    [Fact]
    public void SequenceAllocator_NodeAfterIf_ReceivesStableSequenceAcrossBranches()
    {
        // Div(If(...), Span("Always"))
        // Span starts at 1 + Width(If) regardless of which branch If took.
        var then = Span(ExpressionTemplate.Literal("\"yes\""));
        var otherwise = Button(
            ExpressionTemplate.Literal("\"no\""),
            ExpressionTemplate.Literal("() => { }"));
        var ifNode = new IfNode(ExpressionTemplate.Literal("_visible"), then, otherwise);
        var spanNode = Span(ExpressionTemplate.Literal("\"always\""));
        var div = Div(ImmutableArray.Create<RenderNode>(ifNode, spanNode));

        int expectedDivWidth = 1 + SequenceAllocator.Width(ifNode) + SequenceAllocator.Width(spanNode);
        Assert.Equal(expectedDivWidth, SequenceAllocator.Width(div));
    }

    [Fact]
    public void SequenceAllocator_ExpansionNode_ConsumesOnlyBodyWidth()
    {
        var node = new ExpansionNode(
            ImmutableArray.Create(new LocalBinding(
                "global::System.String",
                "__bcf_arg_1_0",
                ExpressionTemplate.Literal("GetLabel()"))),
            Span(ExpressionTemplate.Literal("__bcf_arg_1_0")));

        Assert.Equal(2, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_ForEachNode_HasWidthOfOnePlusContentWidth()
    {
        var content = Span(ExpressionTemplate.Literal("__bcf_item_0.Title"));

        var node = new ForEachNode(
            Source: ExpressionTemplate.Literal("_items"),
            Key: ExpressionTemplate.Literal("__bcf_item_0.Id"),
            Content: content,
            LoopVariableName: "__bcf_item_0");

        Assert.Equal(1 + SequenceAllocator.Width(content), SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_DecoratedSpanElement_HasWidthThree()
    {
        var node = Span(
            ExpressionTemplate.Literal("\"hello\""),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"badge\"")));

        Assert.Equal(3, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_DecoratedSpanElement_WidthIsIndependentOfClassCount()
    {
        var one = Span(
            ExpressionTemplate.Literal("\"hello\""),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"a\"")));
        var three = Span(
            ExpressionTemplate.Literal("\"hello\""),
            ImmutableArray.Create(
                ExpressionTemplate.Literal("\"a\""),
                ExpressionTemplate.Literal("\"b\""),
                ExpressionTemplate.Literal("\"c\"")));

        Assert.Equal(SequenceAllocator.Width(one), SequenceAllocator.Width(three));
        Assert.Equal(3, SequenceAllocator.Width(three));
    }

    [Fact]
    public void SequenceAllocator_DecoratedButtonElement_HasWidthFour()
    {
        var node = Button(
            ExpressionTemplate.Literal("\"label\""),
            ExpressionTemplate.Literal("() => { }"),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"btn\"")));

        Assert.Equal(4, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_DecoratedDivElement_AddsOneForClassAttribute()
    {
        var child = Span(ExpressionTemplate.Literal("\"a\""));
        var undecorated = Div(ImmutableArray.Create<RenderNode>(child));
        var decorated = Div(
            ImmutableArray.Create<RenderNode>(child),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"row\"")));

        Assert.Equal(SequenceAllocator.Width(undecorated) + 1, SequenceAllocator.Width(decorated));
    }

    [Fact]
    public void SequenceAllocator_Attributes_AddOneFrameEach()
    {
        var attrs = ImmutableArray.Create(
            new AttributeTemplate("href", ExpressionTemplate.Literal("\"/a\"")),
            new AttributeTemplate("id", ExpressionTemplate.Literal("\"x\"")));
        // 1 (OpenElement) + 2 attributes = 3
        Assert.Equal(3, SequenceAllocator.Width(DivWithAttrs(attrs)));
    }

    [Fact]
    public void SequenceAllocator_ClassAttributesEvents_SumInOrder()
    {
        var node = new ElementNode(
            "a",
            ImmutableArray.Create(ExpressionTemplate.Literal("\"nav\"")),                 // class fold -> 1
            ImmutableArray.Create(new AttributeTemplate("href", ExpressionTemplate.Literal("\"/a\""))), // attr -> 1
            ImmutableArray.Create(new EventTemplate("onclick", ExpressionTemplate.Literal("() => { }"))), // event -> 1
            ImmutableArray.Create<RenderNode>(new TextContentNode(ExpressionTemplate.Literal("\"Home\"")))); // child -> 1
        // 1 (OpenElement) + 1 class + 1 attr + 1 event + 1 child = 5
        Assert.Equal(5, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_RawMarkup_HasWidthOne() =>
        Assert.Equal(1, SequenceAllocator.Width(new RawMarkupNode(ExpressionTemplate.Literal("\"<b>x</b>\""))));

    [Fact]
    public void SequenceAllocator_EmptyFragment_HasWidthZero() =>
        Assert.Equal(0, SequenceAllocator.Width(new FragmentNode(ImmutableArray<RenderNode>.Empty)));

    [Fact]
    public void SequenceAllocator_Fragment_SumsChildWidths()
    {
        // two span children, each width 2 (OpenElement + AddContent) => 4.
        var node = new FragmentNode(ImmutableArray.Create<RenderNode>(
            Span(ExpressionTemplate.Literal("\"a\"")),
            Span(ExpressionTemplate.Literal("\"b\""))));
        Assert.Equal(4, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_ComponentWithOneSlot_CountsSlotParameterPlusContent()
    {
        // 1 OpenComponent + 1 scalar parameter + 1 slot AddComponentParameter + span (OpenElement+AddContent = 2).
        var node = new ComponentNode(
            "global::X.C",
            ImmutableArray.Create(new ComponentParameter("Title", ExpressionTemplate.Literal("\"t\""))),
            ImmutableArray.Create(
                new ComponentSlotNode("ChildContent", Span(ExpressionTemplate.Literal("\"x\"")))));

        Assert.Equal(5, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_ComponentWithTwoSlots_CountsBoth()
    {
        var node = new ComponentNode(
            "global::X.C",
            EquatableArray<ComponentParameter>.Empty,
            ImmutableArray.Create(
                new ComponentSlotNode("ChildContent", new TextContentNode(ExpressionTemplate.Literal("\"a\""))),
                new ComponentSlotNode("Footer", new TextContentNode(ExpressionTemplate.Literal("\"b\"")))));

        // 1 OpenComponent + (1 + 1) + (1 + 1)
        Assert.Equal(5, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_ComponentWithNoSlots_IsUnchanged()
    {
        var node = new ComponentNode(
            "global::X.C",
            ImmutableArray.Create(new ComponentParameter("Title", ExpressionTemplate.Literal("\"t\""))));

        Assert.Equal(2, SequenceAllocator.Width(node));
    }
}

using System.Collections.Immutable;
using System.Linq;
using BlazorCompose.Compiler;
using BlazorCompose.Compiler.Generation;

namespace BlazorCompose.Compiler.Tests;

public sealed class SequenceAllocatorTests
{
    private static ElementNode Span(ExpressionTemplate content, EquatableArray<ExpressionTemplate> classes = default) =>
        new("span", classes, default, ImmutableArray.Create<RenderNode>(new TextContentNode(content)));

    private static ElementNode Button(
        ExpressionTemplate label,
        ExpressionTemplate handler,
        EquatableArray<ExpressionTemplate> classes = default) =>
        new(
            "button",
            classes,
            ImmutableArray.Create(new EventTemplate(ExpressionTemplate.Literal("\"onclick\""), handler)),
            ImmutableArray.Create<RenderNode>(new TextContentNode(label)));

    private static ElementNode Div(
        EquatableArray<RenderNode> children, EquatableArray<ExpressionTemplate> classes = default) =>
        new("div", classes, default, children);

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
        // OpenRegion(k) + then contents — else branch absent
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
                "__bc_arg_1_0",
                ExpressionTemplate.Literal("GetLabel()"))),
            Span(ExpressionTemplate.Literal("__bc_arg_1_0")));

        Assert.Equal(2, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_ForEachNode_HasWidthOfOnePlusContentWidth()
    {
        var content = Span(ExpressionTemplate.Literal("__bc_item_0.Title"));

        var node = new ForEachNode(
            Source: ExpressionTemplate.Literal("_items"),
            Key: ExpressionTemplate.Literal("__bc_item_0.Id"),
            Content: content,
            LoopVariableName: "__bc_item_0");

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
}

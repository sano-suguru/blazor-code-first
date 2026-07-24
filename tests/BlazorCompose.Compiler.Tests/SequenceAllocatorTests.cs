using System.Collections.Immutable;
using System.Linq;
using BlazorCompose.Compiler;
using BlazorCompose.Compiler.Generation;

namespace BlazorCompose.Compiler.Tests;

public sealed class SequenceAllocatorTests
{
    [Fact]
    public void SequenceAllocator_TextNode_HasWidthTwo()
    {
        Assert.Equal(2, SequenceAllocator.Width(new TextNode(ExpressionTemplate.Literal("\"hello\""))));
    }

    [Fact]
    public void SequenceAllocator_ButtonNode_HasWidthThree()
    {
        Assert.Equal(3, SequenceAllocator.Width(new ButtonNode(
            ExpressionTemplate.Literal("\"label\""),
            ExpressionTemplate.Literal("() => { }"))));
    }

    [Fact]
    public void SequenceAllocator_VStackNode_HasWidthOfOnePlusChildWidths()
    {
        var children = ImmutableArray.Create<RenderNode>(
            new TextNode(ExpressionTemplate.Literal("\"a\"")),
            new ButtonNode(
                ExpressionTemplate.Literal("\"b\""),
                ExpressionTemplate.Literal("() => { }")));

        Assert.Equal(
            1 + children.Sum(SequenceAllocator.Width),
            SequenceAllocator.Width(new VStackNode(children)));
    }

    [Fact]
    public void SequenceAllocator_EmptyVStack_HasWidthOne()
    {
        Assert.Equal(1, SequenceAllocator.Width(new VStackNode(ImmutableArray<RenderNode>.Empty)));
    }

    // -----------------------------------------------------------------------
    // IfNode
    // -----------------------------------------------------------------------

    [Fact]
    public void SequenceAllocator_IfNodeWithoutElse_HasWidthOfOnePlusThenBranch()
    {
        // OpenRegion(k) + then contents — else branch absent
        var then = new TextNode(ExpressionTemplate.Literal("\"yes\""));
        Assert.Equal(
            1 + SequenceAllocator.Width(then),
            SequenceAllocator.Width(new IfNode(ExpressionTemplate.Literal("_visible"), then, null)));
    }

    [Fact]
    public void SequenceAllocator_IfNodeWithElse_HasWidthOfOnePlusBothBranches()
    {
        // OpenRegion(k) + then contents + else contents
        var then = new TextNode(ExpressionTemplate.Literal("\"yes\""));
        var otherwise = new ButtonNode(
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
        var then = new TextNode(ExpressionTemplate.Literal("\"yes\""));
        var otherwise = new TextNode(ExpressionTemplate.Literal("\"no\""));
        int thenW = SequenceAllocator.Width(then);
        int elseW = SequenceAllocator.Width(otherwise);
        Assert.Equal(
            1 + thenW + elseW,
            SequenceAllocator.Width(new IfNode(ExpressionTemplate.Literal("_v"), then, otherwise)));
    }

    [Fact]
    public void SequenceAllocator_NodeAfterIf_ReceivesStableSequenceAcrossBranches()
    {
        // VStack(If(...), Text("Always"))
        // Text starts at 1 + Width(If) regardless of which branch If took.
        var then = new TextNode(ExpressionTemplate.Literal("\"yes\""));
        var otherwise = new ButtonNode(
            ExpressionTemplate.Literal("\"no\""),
            ExpressionTemplate.Literal("() => { }"));
        var ifNode = new IfNode(ExpressionTemplate.Literal("_visible"), then, otherwise);
        var textNode = new TextNode(ExpressionTemplate.Literal("\"always\""));
        var vstack = new VStackNode(ImmutableArray.Create<RenderNode>(ifNode, textNode));

        int expectedVStackWidth = 1 + SequenceAllocator.Width(ifNode) + SequenceAllocator.Width(textNode);
        Assert.Equal(expectedVStackWidth, SequenceAllocator.Width(vstack));
    }

    [Fact]
    public void SequenceAllocator_ExpansionNode_ConsumesOnlyBodyWidth()
    {
        var node = new ExpansionNode(
            ImmutableArray.Create(new LocalBinding(
                "global::System.String",
                "__bc_arg_1_0",
                ExpressionTemplate.Literal("GetLabel()"))),
            new TextNode(ExpressionTemplate.Literal("__bc_arg_1_0")));

        Assert.Equal(2, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_ForEachNode_HasWidthOfOnePlusContentWidth()
    {
        var content = new TextNode(ExpressionTemplate.Literal("__bc_item_0.Title"));

        var node = new ForEachNode(
            Source: ExpressionTemplate.Literal("_items"),
            Key: ExpressionTemplate.Literal("__bc_item_0.Id"),
            Content: content,
            LoopVariableName: "__bc_item_0");

        Assert.Equal(1 + SequenceAllocator.Width(content), SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_DecoratedTextNode_HasWidthThree()
    {
        var node = new TextNode(
            ExpressionTemplate.Literal("\"hello\""),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"badge\"")));

        Assert.Equal(3, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_DecoratedTextNode_WidthIsIndependentOfClassCount()
    {
        var one = new TextNode(
            ExpressionTemplate.Literal("\"hello\""),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"a\"")));
        var three = new TextNode(
            ExpressionTemplate.Literal("\"hello\""),
            ImmutableArray.Create(
                ExpressionTemplate.Literal("\"a\""),
                ExpressionTemplate.Literal("\"b\""),
                ExpressionTemplate.Literal("\"c\"")));

        Assert.Equal(SequenceAllocator.Width(one), SequenceAllocator.Width(three));
        Assert.Equal(3, SequenceAllocator.Width(three));
    }

    [Fact]
    public void SequenceAllocator_DecoratedButtonNode_HasWidthFour()
    {
        var node = new ButtonNode(
            ExpressionTemplate.Literal("\"label\""),
            ExpressionTemplate.Literal("() => { }"),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"btn\"")));

        Assert.Equal(4, SequenceAllocator.Width(node));
    }

    [Fact]
    public void SequenceAllocator_DecoratedVStackNode_AddsOneForClassAttribute()
    {
        var child = new TextNode(ExpressionTemplate.Literal("\"a\""));
        var undecorated = new VStackNode(ImmutableArray.Create<RenderNode>(child));
        var decorated = new VStackNode(
            ImmutableArray.Create<RenderNode>(child),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"row\"")));

        Assert.Equal(SequenceAllocator.Width(undecorated) + 1, SequenceAllocator.Width(decorated));
    }
}

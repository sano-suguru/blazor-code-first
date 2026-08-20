using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class RenderViewEmitterForEachTests
{
    private static ElementNode Span(ExpressionTemplate content) =>
        new("span", default, default, default, ImmutableArray.Create<RenderNode>(new TextContentNode(content)));

    [Fact]
    public void Emit_ForEachRoot_EmitsKeyedForeachRegionWithContentAtSeqPlusOne()
    {
        var node = new ForEachNode(
            Source: ExpressionTemplate.Literal("_items"),
            Key: ExpressionTemplate.Literal("__bcf_item_0.Id"),
            Content: Span(ExpressionTemplate.Literal("__bcf_item_0.Title")),
            LoopVariableName: "__bcf_item_0");

        var model = new ComponentModel(
            HintName: "T.g.cs",
            ClassName: "T",
            TypeParameters: default,
            Namespace: null,
            RootNode: node);

        var generated = RenderViewEmitter.Emit(model).ToString();

        Assert.Contains("__builder.OpenRegion(0);", generated);
        Assert.Contains("foreach (var __bcf_item_0 in _items)", generated);
        Assert.Contains("__builder.SetKey(__bcf_item_0.Id);", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\");", generated);
        Assert.Contains("__builder.AddContent(2, __bcf_item_0.Title);", generated);
        Assert.Contains("__builder.CloseRegion();", generated);
    }

    [Fact]
    public void Emit_ForEachRoot_EmitsSetKeyAfterContentRootOpenElement()
    {
        // Regression guard for the render-time defect ARCHITECTURE.md §2.7(B) records: Blazor's SetKey applies to the currently open
        // element/component frame, so SetKey emitted while the open frame is the OpenRegion frame
        // throws "Cannot set a key on a frame of type Region." at render time. SetKey must therefore
        // appear AFTER the content root's OpenElement, not merely be present in the output.
        var node = new ForEachNode(
            Source: ExpressionTemplate.Literal("_items"),
            Key: ExpressionTemplate.Literal("__bcf_item_0.Id"),
            Content: Span(ExpressionTemplate.Literal("__bcf_item_0.Title")),
            LoopVariableName: "__bcf_item_0");

        var model = new ComponentModel(
            HintName: "T.g.cs",
            ClassName: "T",
            TypeParameters: default,
            Namespace: null,
            RootNode: node);

        var generated = RenderViewEmitter.Emit(model).ToString();

        int openIdx = generated.IndexOf("__builder.OpenElement(1, \"span\");", System.StringComparison.Ordinal);
        int keyIdx = generated.IndexOf("__builder.SetKey(", System.StringComparison.Ordinal);

        Assert.True(openIdx >= 0, "content root OpenElement should be emitted");
        Assert.True(keyIdx > openIdx, "SetKey must be emitted after the content root's OpenElement");
    }
}

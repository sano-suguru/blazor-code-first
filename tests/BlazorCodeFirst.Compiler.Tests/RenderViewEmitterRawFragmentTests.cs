using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class RenderViewEmitterRawFragmentTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderViewEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", TypeParameters: default, Namespace: null, RootNode: root)).ToString();

    private static ElementNode Span(ExpressionTemplate content) =>
        new("span", default, default, default,
            ImmutableArray.Create<RenderNode>(new TextContentNode(content)));

    [Fact]
    public void EmitRawMarkup_EmitsAddMarkupContentAtSeq()
    {
        var code = EmitRoot(new RawMarkupNode(ExpressionTemplate.Literal("\"<b>x</b>\"")));
        Assert.Contains("__builder.AddMarkupContent(0, \"<b>x</b>\");", code);
    }

    [Fact]
    public void EmitRawMarkup_ValueIsEmittedVerbatim()
    {
        // A field reference is emitted verbatim (no this-qualification), matching how the generator
        // lowers other expressions (see GeneratorTests: AddContent(2, $"Count: {_count}")).
        var code = EmitRoot(new RawMarkupNode(ExpressionTemplate.Literal("_html")));
        Assert.Contains("__builder.AddMarkupContent(0, _html);", code);
    }

    [Fact]
    public void EmitFragment_EmitsChildrenInSequence_NoOpenOrCloseElement()
    {
        var node = new FragmentNode(ImmutableArray.Create<RenderNode>(
            Span(ExpressionTemplate.Literal("\"a\"")),
            Span(ExpressionTemplate.Literal("\"b\""))));

        var code = EmitRoot(node);

        // span(0)->"a"(1), span(2)->"b"(3); no wrapping element opened for the fragment itself.
        Assert.Contains("__builder.OpenElement(0, \"span\");", code);
        Assert.Contains("__builder.AddContent(1, \"a\");", code);
        Assert.Contains("__builder.OpenElement(2, \"span\");", code);
        Assert.Contains("__builder.AddContent(3, \"b\");", code);
    }

    [Fact]
    public void EmitEmptyFragment_EmitsNothing()
    {
        var code = EmitRoot(new FragmentNode(ImmutableArray<RenderNode>.Empty));
        Assert.DoesNotContain("__builder.OpenElement", code);
        Assert.DoesNotContain("__builder.AddContent", code);
        Assert.DoesNotContain("__builder.AddMarkupContent", code);
    }

    [Fact]
    public void EmitFragment_EmittedSequenceArguments_AreDense()
    {
        // Regression guard for the emitter's sequence arithmetic on the wrapper-less node: a fragment
        // opens no frame of its own, so its children must number as if they were the parent's.
        var node = new FragmentNode(ImmutableArray.Create<RenderNode>(
            Span(ExpressionTemplate.Literal("\"a\"")),
            new RawMarkupNode(ExpressionTemplate.Literal("\"<i>y</i>\"")),
            Span(ExpressionTemplate.Literal("\"b\""))));

        SequenceArguments.AssertDense(EmitRoot(node));
    }
}

using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class RenderViewEmitterFragmentContentTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderViewEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", TypeParameters: default, Namespace: null, RootNode: root)).ToString();

    [Fact]
    public void EmitFragmentContent_EmitsAddContentAtSeq()
    {
        var code = EmitRoot(new RenderFragmentContentNode(ExpressionTemplate.Literal("Body")));
        Assert.Contains("__builder.AddContent(0, Body);", code);
    }

    [Fact]
    public void EmitFragmentContent_ValueIsEmittedVerbatim()
    {
        var code = EmitRoot(new RenderFragmentContentNode(ExpressionTemplate.Literal("ChildContent")));
        Assert.Contains("__builder.AddContent(0, ChildContent);", code);
    }

    [Fact]
    public void FragmentContent_ConsumesOneSequenceNumber_RegardlessOfRuntimeNullness()
    {
        // AddContent(seq, fragment) is always called; it emits a Region frame only when the fragment is
        // non-null. So the node consumes exactly one sequence number and sibling sequences never depend
        // on runtime state.
        var code = EmitRoot(new RenderFragmentContentNode(ExpressionTemplate.Literal("Body")));

        Assert.Equal(0, Assert.Single(SequenceArguments.InTextOrder(code)));
    }

    [Fact]
    public void EmitFragmentContent_InsideElement_TakesOneSequenceSlot()
    {
        var node = new ElementNode("main", default, default, default,
            ImmutableArray.Create<RenderNode>(
                new RenderFragmentContentNode(ExpressionTemplate.Literal("Body")),
                new TextContentNode(ExpressionTemplate.Literal("\"after\""))));

        var code = EmitRoot(node);

        Assert.Contains("__builder.OpenElement(0, \"main\");", code);
        Assert.Contains("__builder.AddContent(1, Body);", code);
        Assert.Contains("__builder.AddContent(2, \"after\");", code);
    }

    [Fact]
    public void Emitter_RenderFragmentAtTheRootOfChrome_EmitsAddContentAtSequenceZero()
    {
        // Every other fragment test places the fragment as a child, inside an If, or as ForEach content.
        // The root case has no wrapping element: EmitNode is called with startSeq 0 and the node's emit
        // does not care about nesting, so it is correct by construction, and was untested.
        const string source = """
            using BlazorCodeFirst;

            public partial class Shell : ChromeLayoutBase
            {
                protected override View Chrome => Body;
            }
            """;

        var generated = Assert.Single(CompilationTestHost.RunGenerator(source).GeneratedSources)
            .SourceText.ToString();

        Assert.Contains("__builder.AddContent(0, Body);", generated);
    }
}

using System.Collections.Immutable;
using BlazorCodeFirst.Compiler;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class RenderViewEmitterComponentTests
{
    [Fact]
    public void Emit_ComponentWithParameters_EmitsOpenComponentAddParametersAndClose()
    {
        var node = new ComponentNode(
            TypeName: "global::MyApp.Counter",
            Parameters: ImmutableArray.Create(
                new ComponentParameter("Start", ExpressionTemplate.Literal("5")),
                new ComponentParameter("Label", ExpressionTemplate.Literal("\"hi\""))));

        var model = new ComponentModel("T.g.cs", "T", default, null, node);

        var generated = RenderViewEmitter.Emit(model).ToString();

        Assert.Contains("__builder.OpenComponent<global::MyApp.Counter>(0);", generated);
        Assert.Contains("__builder.AddComponentParameter(1, \"Start\", 5);", generated);
        Assert.Contains("__builder.AddComponentParameter(2, \"Label\", \"hi\");", generated);
        Assert.Contains("__builder.CloseComponent();", generated);
    }

    [Fact]
    public void Emit_ComponentWithNoParameters_EmitsOpenAndCloseOnly()
    {
        var node = new ComponentNode("global::MyApp.Widget", ImmutableArray<ComponentParameter>.Empty);
        var model = new ComponentModel("T.g.cs", "T", default, null, node);

        var generated = RenderViewEmitter.Emit(model).ToString();

        Assert.Contains("__builder.OpenComponent<global::MyApp.Widget>(0);", generated);
        Assert.Contains("__builder.CloseComponent();", generated);
        Assert.DoesNotContain("AddComponentParameter", generated);
    }

    [Fact]
    public void Emit_ComponentWithSlot_EmitsFragmentLambdaWithFlatSequenceContinuation()
    {
        var node = new ComponentNode(
            TypeName: "global::MyApp.Card",
            Parameters: ImmutableArray.Create(
                new ComponentParameter("Title", ExpressionTemplate.Literal("\"t\""))),
            Slots: ImmutableArray.Create(
                new ComponentSlotNode(
                    "ChildContent",
                    new ElementNode(
                        "div", default, default, default,
                        ImmutableArray.Create<RenderNode>(
                            new TextContentNode(ExpressionTemplate.Literal("\"x\"")))))));

        var model = new ComponentModel("T.g.cs", "T", default, null, node);

        var generated = RenderViewEmitter.Emit(model).ToString();

        Assert.Contains("__builder.OpenComponent<global::MyApp.Card>(0);", generated);
        Assert.Contains("__builder.AddComponentParameter(1, \"Title\", \"t\");", generated);
        Assert.Contains(
            "__builder.AddComponentParameter(2, \"ChildContent\", "
                + "(global::Microsoft.AspNetCore.Components.RenderFragment)((__builder) =>",
            generated);
        // Numbering continues flatly inside the lambda: restarting at 0 destroys component state
        // against a host that invokes the fragment directly (spec fact 12).
        Assert.Contains("__builder.OpenElement(3, \"div\");", generated);
        Assert.Contains("__builder.AddContent(4, \"x\");", generated);
        Assert.Contains("__builder.CloseComponent();", generated);
    }

    [Fact]
    public void Emit_ComponentWithSlotAndFollowingSibling_GivesSiblingThePostSlotSequence()
    {
        var component = new ComponentNode(
            "global::MyApp.Card",
            EquatableArray<ComponentParameter>.Empty,
            ImmutableArray.Create(
                new ComponentSlotNode("ChildContent", new TextContentNode(ExpressionTemplate.Literal("\"x\"")))));

        var root = new ElementNode(
            "div", default, default, default,
            ImmutableArray.Create<RenderNode>(
                component, new TextContentNode(ExpressionTemplate.Literal("\"after\""))));

        var generated = RenderViewEmitter.Emit(
            new ComponentModel("T.g.cs", "T", default, null, root)).ToString();

        // div=0, OpenComponent=1, slot parameter=2, slot content=3, so the sibling must be 4.
        Assert.Contains("__builder.AddContent(4, \"after\");", generated);
    }

    [Fact]
    public void Emit_ComponentWithSlot_SequenceArgumentCountEqualsWidth()
    {
        var node = new ComponentNode(
            "global::MyApp.Card",
            ImmutableArray.Create(new ComponentParameter("Title", ExpressionTemplate.Literal("\"t\""))),
            ImmutableArray.Create(
                new ComponentSlotNode("ChildContent", new TextContentNode(ExpressionTemplate.Literal("\"x\"")))));

        var generated = RenderViewEmitter.Emit(
            new ComponentModel("T.g.cs", "T", default, null, node)).ToString();

        // Every sequence-consuming call is counted; a drift between SequenceAllocator and the emitter
        // shows up here as a missing or extra call. Mirrors EmitElement_SequenceArgumentCount_EqualsWidth
        // in RenderViewEmitterDecorationTests:122. OpenComponent needs its own alternative because the
        // type argument sits between the method name and the paren.
        int seqCalls = System.Text.RegularExpressions.Regex.Count(
            generated,
            @"__builder\.(OpenElement|AddAttribute|AddContent|AddMarkupContent|OpenRegion|AddComponentParameter)\(|__builder\.OpenComponent<");

        Assert.Equal(Generation.SequenceAllocator.Width(node), seqCalls);
    }
}

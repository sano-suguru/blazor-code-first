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
    public void Emit_ComponentWithContextIgnoredGenericSlot_EmitsTypedOuterAndInnerFragmentLambdas()
    {
        var node = new ComponentNode(
            "global::MyApp.Grid",
            ImmutableArray.Create(new ComponentParameter("Dense", ExpressionTemplate.Literal("true"))),
            ImmutableArray.Create(
                new ComponentSlotNode(
                    "RowTemplate",
                    new TextContentNode(ExpressionTemplate.Literal("\"x\"")))
                {
                    Kind = ComponentSlotKind.GenericContextIgnored,
                    ContextTypeName = "global::System.Int32",
                }));

        var generated = RenderViewEmitter.Emit(
            new ComponentModel("T.g.cs", "T", default, null, node)).ToString();

        Assert.Contains(
            "__builder.AddComponentParameter(2, \"RowTemplate\", " +
                "(global::Microsoft.AspNetCore.Components.RenderFragment<global::System.Int32>)" +
                "((_) => (__builder) =>",
            generated);
        Assert.Contains("__builder.AddContent(3, \"x\");", generated);
        SequenceArguments.AssertDense(generated);
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
    public void Emit_ComponentWithSlot_EmittedSequenceArgumentsAreDense()
    {
        var node = new ComponentNode(
            "global::MyApp.Card",
            ImmutableArray.Create(new ComponentParameter("Title", ExpressionTemplate.Literal("\"t\""))),
            ImmutableArray.Create(
                new ComponentSlotNode("ChildContent", new TextContentNode(ExpressionTemplate.Literal("\"x\"")))));

        var generated = RenderViewEmitter.Emit(
            new ComponentModel("T.g.cs", "T", default, null, node)).ToString();

        // A slot's frames continue the enclosing flat counter, so a drift in the slot arithmetic shows up
        // as a gap or a repeat in the emitted run. Mirrors EmitElement_EmittedSequenceArguments_AreDense
        // in RenderViewEmitterDecorationTests.
        SequenceArguments.AssertDense(generated);
    }
}

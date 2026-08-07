using System;
using System.Collections.Immutable;
using BlazorCodeFirst.Compiler;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class RenderViewEmitterDecorationTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderViewEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", TypeParameters: default, Namespace: null, RootNode: root)).ToString();

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

    [Fact]
    public void Emit_SingleClassSpan_EmitsClassAttributeVerbatimAtSeqPlusOne()
    {
        var node = Span(
            ExpressionTemplate.Literal("\"Hi\""),
            ImmutableArray.Create(ExpressionTemplate.Literal("\"badge\"")));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.OpenElement(0, \"span\");", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"badge\");", generated);
        Assert.Contains("__builder.AddContent(2, \"Hi\");", generated);
    }

    [Fact]
    public void Emit_MultipleClassesSpan_FoldsIntoSingleParenthesizedConcatenation()
    {
        var node = Span(
            ExpressionTemplate.Literal("\"Hi\""),
            ImmutableArray.Create(
                ExpressionTemplate.Literal("\"a\""),
                ExpressionTemplate.Literal("\"b\"")));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.AddAttribute(1, \"class\", (\"a\") + \" \" + (\"b\"));", generated);
        Assert.Contains("__builder.AddContent(2, \"Hi\");", generated);
    }

    [Fact]
    public void Emit_DynamicClassSpan_EmitsExpressionAsAttributeValue()
    {
        var node = Span(
            ExpressionTemplate.Literal("\"Hi\""),
            ImmutableArray.Create(ExpressionTemplate.Literal("_on ? \"on\" : \"off\"")));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.AddAttribute(1, \"class\", _on ? \"on\" : \"off\");", generated);
    }

    [Fact]
    public void Emit_DecoratedButton_EmitsClassBeforeOnclick()
    {
        var node = Button(
            ExpressionTemplate.Literal("\"OK\""),
            ExpressionTemplate.Literal("OnOk"),
            ImmutableArray.Create(
                ExpressionTemplate.Literal("\"btn\""),
                ExpressionTemplate.Literal("\"primary\"")));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.AddAttribute(1, \"class\", (\"btn\") + \" \" + (\"primary\"));", generated);
        Assert.Contains("__builder.AddAttribute(2, \"onclick\", ", generated);
        Assert.Contains("__builder.AddContent(3, \"OK\");", generated);

        int classIdx = generated.IndexOf("\"class\"", StringComparison.Ordinal);
        int onclickIdx = generated.IndexOf("\"onclick\"", StringComparison.Ordinal);
        Assert.True(classIdx >= 0 && onclickIdx >= 0 && classIdx < onclickIdx,
            "class attribute must be emitted before onclick");
    }

    [Fact]
    public void Emit_UndecoratedSpan_EmitsNoClassAttribute()
    {
        var generated = EmitRoot(Span(ExpressionTemplate.Literal("\"Hi\"")));

        Assert.DoesNotContain("\"class\"", generated);
        Assert.Contains("__builder.AddContent(1, \"Hi\");", generated);
    }

    [Fact]
    public void EmitElement_EmitsClassThenAttributesThenEventsThenChildren_InSequenceOrder()
    {
        var node = new ElementNode(
            "a",
            ImmutableArray.Create(ExpressionTemplate.Literal("\"nav\"")),
            ImmutableArray.Create(new AttributeTemplate("href", ExpressionTemplate.Literal("\"/a\""))),
            ImmutableArray.Create(new EventTemplate("onclick", ExpressionTemplate.Literal("() => { }"))),
            ImmutableArray.Create<RenderNode>(new TextContentNode(ExpressionTemplate.Literal("\"Home\""))));

        var code = EmitRoot(node);

        Assert.Contains("__builder.OpenElement(0, \"a\");", code);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"nav\");", code);
        Assert.Contains("__builder.AddAttribute(2, \"href\", \"/a\");", code);
        Assert.Contains(
            "__builder.AddAttribute(3, \"onclick\", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => { }));",
            code);
        Assert.Contains("__builder.AddContent(4, \"Home\");", code);
    }

    [Fact]
    public void EmitElement_EmittedSequenceArguments_AreDense()
    {
        // Regression guard for the emitter's sequence arithmetic: the numbers emitted for a decorated
        // element with attributes, events, and nested children must form a dense run with no gap and no
        // repeat. SequenceArguments.AssertDense records why this replaced a comparison against an
        // independently computed width (#69).
        var node = new ElementNode(
            "a",
            ImmutableArray.Create(ExpressionTemplate.Literal("\"nav\"")),
            ImmutableArray.Create(
                new AttributeTemplate("href", ExpressionTemplate.Literal("\"/a\"")),
                new AttributeTemplate("id", ExpressionTemplate.Literal("\"x\""))),
            ImmutableArray.Create(new EventTemplate("onclick", ExpressionTemplate.Literal("() => { }"))),
            ImmutableArray.Create<RenderNode>(
                new TextContentNode(ExpressionTemplate.Literal("\"Home\"")),
                new ElementNode("span", default, default, default,
                    ImmutableArray.Create<RenderNode>(new TextContentNode(ExpressionTemplate.Literal("\"!\""))))));

        SequenceArguments.AssertDense(EmitRoot(node));
    }

    private static ElementNode BoundInput(BindTemplate bind) =>
        new("input", default, default, default, default) { Bind = bind };

    [Fact]
    public void Emit_BoundElement_EmitsValueThenBinderThenUpdatesName()
    {
        // The binder is spelled as the static call it is. CreateBinder is an extension method on
        // EventCallbackFactory, and the generated file carries no using directives, so the instance
        // spelling fails with CS1061 there. This test does not compile its output, so the spelling is
        // held to the one RenderExpressionAnalyzer builds and HtmlBindGeneratorTests compiles.
        const string binder =
            "global::Microsoft.AspNetCore.Components.EventCallbackFactoryBinderExtensions.CreateBinder("
            + "global::Microsoft.AspNetCore.Components.EventCallback.Factory, this, "
            + "__value => _name = __value, _name)";

        var node = BoundInput(new BindTemplate(
            "value",
            "oninput",
            ExpressionTemplate.Literal("_name"),
            ExpressionTemplate.Literal(binder)));

        var generated = EmitRoot(node);

        Assert.Contains("__builder.OpenElement(0, \"input\");", generated);
        Assert.Contains("__builder.AddAttribute(1, \"value\", _name);", generated);
        Assert.Contains($"__builder.AddAttribute(2, \"oninput\", {binder});", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
        Assert.Contains("__builder.CloseElement();", generated);
    }

    [Fact]
    public void Emit_BoundElementWithOtherDecorations_NumbersBindFramesAfterThem()
    {
        var node = new ElementNode(
            "input",
            ImmutableArray.Create(ExpressionTemplate.Literal("\"field\"")),
            ImmutableArray.Create(new AttributeTemplate("type", ExpressionTemplate.Literal("\"text\""))),
            default,
            default)
        {
            Bind = new BindTemplate(
                "value",
                "oninput",
                ExpressionTemplate.Literal("_name"),
                ExpressionTemplate.Literal("BINDER")),
        };

        var generated = EmitRoot(node);

        Assert.Contains("__builder.AddAttribute(1, \"class\", \"field\");", generated);
        Assert.Contains("__builder.AddAttribute(2, \"type\", \"text\");", generated);
        Assert.Contains("__builder.AddAttribute(3, \"value\", _name);", generated);
        Assert.Contains("__builder.AddAttribute(4, \"oninput\", BINDER);", generated);
        Assert.Contains("__builder.SetUpdatesAttributeName(\"value\");", generated);
    }

    [Fact]
    public void Emit_UnboundElement_EmitsNoUpdatesAttributeName()
    {
        var generated = EmitRoot(Span(ExpressionTemplate.Literal("\"Hi\"")));

        Assert.DoesNotContain("SetUpdatesAttributeName", generated);
    }
}

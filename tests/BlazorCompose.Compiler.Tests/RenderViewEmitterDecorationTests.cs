using System;
using System.Collections.Immutable;
using BlazorCompose.Compiler;
using BlazorCompose.Compiler.Generation;

namespace BlazorCompose.Compiler.Tests;

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
    public void EmitElement_SequenceArgumentCount_EqualsWidth()
    {
        // Regression guard for the Width/Emit invariant: for a non-branching element tree the number of
        // sequence-consuming builder calls emitted must equal SequenceAllocator.Width(node).
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

        var code = EmitRoot(node);
        int seqCalls = System.Text.RegularExpressions.Regex.Count(
            code,
            @"__builder\.(OpenElement|AddAttribute|AddContent|OpenComponent|AddComponentParameter)\(");

        Assert.Equal(SequenceAllocator.Width(node), seqCalls);
    }
}

using System;
using System.Collections.Immutable;
using BlazorCompose.Compiler;

namespace BlazorCompose.Compiler.Tests;

public sealed class RenderBodyEmitterDecorationTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderBodyEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", Namespace: null, RootNode: root)).ToString();

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
}

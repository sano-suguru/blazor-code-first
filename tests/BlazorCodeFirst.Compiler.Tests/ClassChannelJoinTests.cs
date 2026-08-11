using System.Collections.Immutable;
using BlazorCodeFirst.Compiler;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// What the class channel's join does with the terms it can read at compile time, and with the ones it
/// cannot (#236). The emitter is the seam these are asked through, because the join's product is
/// generated text and reading it anywhere else would test a spelling nothing runs.
/// </summary>
public sealed class ClassChannelJoinTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderViewEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", TypeParameters: default, Namespace: null, RootNode: root))
            .ToString();

    /// <summary>A constant string term, carrying the constant the analyzer would have read.</summary>
    private static ExpressionTemplate Constant(string text) =>
        ExpressionTemplate.Create(
            [new LiteralExpressionSegment($"\"{text}\"")],
            new StringConstant(text));

    /// <summary>A constant null term.</summary>
    private static ExpressionTemplate ConstantNull() =>
        ExpressionTemplate.Create([new LiteralExpressionSegment("null")], new NullConstant());

    /// <summary>A term with no compile-time value, such as a field read.</summary>
    private static ExpressionTemplate Dynamic(string code) => ExpressionTemplate.Literal(code);

    /// <summary>
    /// A span whose classes are <paramref name="classes"/> and whose text is not constant, so nothing
    /// here reaches the static fold and every test below reads the frame path.
    /// </summary>
    private static ElementNode Span(params ExpressionTemplate[] classes) =>
        new("span",
            ImmutableArray.Create(classes),
            default,
            default,
            ImmutableArray.Create<RenderNode>(new TextContentNode(ExpressionTemplate.Literal("_text"))));

    [Fact]
    public void JoinAsCode_WhenATermIsAConstantNull_DropsItFromTheJoin()
    {
        var generated = EmitRoot(Span(Constant("card"), ConstantNull()));

        Assert.Contains("__builder.AddAttribute(1, \"class\", \"card\");", generated);
    }

    [Fact]
    public void JoinAsCode_WhenAdjacentTermsAreConstantStrings_FoldsThemIntoOneLiteral()
    {
        var generated = EmitRoot(Span(Constant("card"), Constant("wide"), Dynamic("_extra")));

        Assert.Contains("\"card wide\"", generated);
    }

    [Fact]
    public void JoinAsCode_WhenConstantsAreNotAdjacent_LeavesThemAsSeparateTerms()
    {
        var generated = EmitRoot(Span(Constant("a"), Dynamic("_x"), Constant("b")));

        Assert.DoesNotContain("\"a b\"", generated);
    }

    [Fact]
    public void JoinAsCode_WhenEveryTermIsAConstantNull_EmitsTheCastTheOverloadNeeds()
    {
        var generated = EmitRoot(Span(ConstantNull(), ConstantNull()));

        Assert.Contains(
            "__builder.AddAttribute(1, \"class\", (global::System.String?)(null));",
            generated);
    }
}

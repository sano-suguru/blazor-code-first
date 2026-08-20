using System.Collections.Immutable;

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
    private static ExpressionTemplate Constant(string text) => ExpressionTemplate.StringLiteral(text);

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

    [Fact]
    public void JoinAsCode_WhenTwoTermsSurvive_CallsTheJoinHelperInsteadOfConcatenating()
    {
        var generated = EmitRoot(Span(Constant("card"), Dynamic("_extra")));

        Assert.Contains(
            "__builder.AddAttribute(1, \"class\", __BlazorCodeFirstJoinClasses((\"card\"), (_extra)));",
            generated);
        Assert.DoesNotContain(" + \" \" + ", generated);
    }

    [Fact]
    public void Emit_WhenAJoinIsEmitted_WritesTheHelperItCalls()
    {
        var generated = EmitRoot(Span(Constant("card"), Dynamic("_extra")));

        Assert.Contains(
            "private static string? __BlazorCodeFirstJoinClasses(string? a0, string? a1) =>",
            generated);
    }

    [Fact]
    public void Emit_WhenTheWidestJoinTakesThreeTerms_WritesTheHelperForTwoTermsAsWell()
    {
        var generated = EmitRoot(Span(Dynamic("_a"), Dynamic("_b"), Dynamic("_c")));

        Assert.Contains("__BlazorCodeFirstJoinClasses(string? a0, string? a1) =>", generated);
        Assert.Contains("__BlazorCodeFirstJoinClasses(string? a0, string? a1, string? a2) =>", generated);
    }

    /// <summary>
    /// The ladder's body at the three arities <c>ClassJoinCandidates</c> transcribes for #239's
    /// measurement. Nothing else ties that transcription to what the compiler writes: the benchmark's
    /// gate compares the two candidates to each other, and both implement the same rule, so a change to
    /// the generation rule would leave it green while the timing described a spelling this generator no
    /// longer emits.
    /// </summary>
    [Fact]
    public void JoinHelperCode_WritesTheBodiesTheBenchmarkTranscribes()
    {
        Assert.Equal(
            """private static string? __BlazorCodeFirstJoinClasses(string? a0, string? a1) => a0 is null ? a1 : a1 is null ? a0 : string.Concat(a0, " ", a1);""",
            ClassChannel.JoinHelperCode(2));
        Assert.Equal(
            """private static string? __BlazorCodeFirstJoinClasses(string? a0, string? a1, string? a2) => a0 is null ? __BlazorCodeFirstJoinClasses(a1, a2) : a1 is null ? __BlazorCodeFirstJoinClasses(a0, a2) : a2 is null ? __BlazorCodeFirstJoinClasses(a0, a1) : string.Concat(a0, " ", a1, " ", a2);""",
            ClassChannel.JoinHelperCode(3));
        Assert.Equal(
            """private static string? __BlazorCodeFirstJoinClasses(string? a0, string? a1, string? a2, string? a3) => a0 is null ? __BlazorCodeFirstJoinClasses(a1, a2, a3) : a1 is null ? __BlazorCodeFirstJoinClasses(a0, a2, a3) : a2 is null ? __BlazorCodeFirstJoinClasses(a0, a1, a3) : a3 is null ? __BlazorCodeFirstJoinClasses(a0, a1, a2) : string.Concat(a0, " ", a1, " ", a2, " ", a3);""",
            ClassChannel.JoinHelperCode(4));
    }

    [Fact]
    public void Emit_WhenNoJoinIsEmitted_WritesNoHelper()
    {
        var generated = EmitRoot(Span(Dynamic("_only")));

        Assert.DoesNotContain("__BlazorCodeFirstJoinClasses", generated);
    }

    /// <summary>
    /// A span whose classes are <paramref name="classes"/> and whose text is constant, so the element is
    /// a candidate for the static fold. Built here rather than through <see cref="Span"/>, whose text is
    /// deliberately not constant.
    /// </summary>
    private static ElementNode FoldableSpan(params ExpressionTemplate[] classes) =>
        new("span",
            ImmutableArray.Create(classes),
            default,
            default,
            ImmutableArray.Create<RenderNode>(new TextContentNode(
                ExpressionTemplate.Create([new LiteralExpressionSegment("\"x\"")], new StringConstant("x")))));

    [Fact]
    public void Fold_WhenAClassTermIsAConstantNull_StillFoldsTheElementToMarkup()
    {
        var generated = EmitRoot(FoldableSpan(Constant("card"), ConstantNull()));

        // The markup text rather than the whole call: whether a lone root folds on its own or only as
        // part of a sibling run is the emitter's business, and this is about what the serializer writes.
        Assert.Contains("<span class=\\\"card\\\">x</span>", generated);
    }

    [Fact]
    public void Fold_WhenEveryClassTermIsAConstantNull_WritesNoClassAttribute()
    {
        var generated = EmitRoot(FoldableSpan(ConstantNull(), ConstantNull()));

        Assert.Contains("<span>x</span>", generated);
    }
}

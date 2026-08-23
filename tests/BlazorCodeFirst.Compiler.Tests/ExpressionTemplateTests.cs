using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Analysis;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ExpressionTemplateTests
{
    [Fact]
    public void ExpressionTemplate_WhenSubstituted_ReplacesOnlyParameterHoles()
    {
        var template = ExpressionTemplate.Create(
            [new LiteralExpressionSegment("$\""), new ParameterHoleExpressionSegment(0), new LiteralExpressionSegment(" value\"")]);

        var result = template.Substitute([new SubstitutedArgument("__bcf_arg_1_0", Constant: null)]);

        Assert.Equal("$\"__bcf_arg_1_0 value\"", result.ToCode());
    }

    [Fact]
    public void ExpressionTemplate_StructurallyEqualTemplates_CompareEqual()
    {
        var left = ExpressionTemplate.Create(
            [new LiteralExpressionSegment("prefix "), new ParameterHoleExpressionSegment(1)]);
        var right = ExpressionTemplate.Create(
            [new LiteralExpressionSegment("prefix "), new ParameterHoleExpressionSegment(1)]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Literal_CarriesNoConstant()
    {
        var template = ExpressionTemplate.Literal("\"a\"");

        Assert.Null(template.Constant);
    }

    [Fact]
    public void Create_CarriesTheSuppliedConstant()
    {
        var template = ExpressionTemplate.Create(
            [new LiteralExpressionSegment("\"a\"")],
            new StringConstant("a"));

        Assert.Equal(new StringConstant("a"), template.Constant);
    }

    [Fact]
    public void ConstantNull_IsDistinctFromNotConstant()
    {
        var constantNull = ExpressionTemplate.Create(
            [new LiteralExpressionSegment("null")],
            new NullConstant());

        Assert.Equal(new NullConstant(), constantNull.Constant);
    }

    /// <summary>
    /// The states are distinct types rather than one nullable string, so "renders nothing" and "renders
    /// something the compiler cannot spell" cannot be read as each other. Conflating them is what let a
    /// constant non-string attribute value fold to markup with the attribute missing (#158).
    /// </summary>
    [Fact]
    public void ConstantNull_IsDistinctFromAConstantThatRendersWithoutAString() =>
        Assert.NotEqual<ConstantInfo>(new NullConstant(), new RuntimeFormattedConstant());

    /// <summary>
    /// A template carrying a constant never contains a parameter hole: a hole is created only for an
    /// identifier bound to a view part parameter, and a parameter reference has no constant value. So
    /// substitution passes the constant through unchanged rather than having to recompute or clear it.
    /// </summary>
    [Fact]
    public void Substitute_PreservesTheConstant()
    {
        var template = ExpressionTemplate.Create(
            [new LiteralExpressionSegment("\"a\"")],
            new StringConstant("a"));

        var substituted = template.Substitute([new SubstitutedArgument("__local", Constant: null)]);

        Assert.Equal(new StringConstant("a"), substituted.Constant);
    }

    /// <summary>
    /// A template that is exactly one parameter hole takes the argument's constant, which is the shape a
    /// view part pass-through produces (Span[title], .Class(cls)). The substituted code becomes the
    /// constant literal rather than the local's name; the value is the same either way.
    /// </summary>
    [Fact]
    public void Substitute_LoneHole_TakesTheArgumentsConstant()
    {
        var template = ExpressionTemplate.Create(
            [new ParameterHoleExpressionSegment(0)]);

        var substituted = template.Substitute([new SubstitutedArgument("__l0", new StringConstant("App"))]);

        Assert.Equal(new StringConstant("App"), substituted.Constant);
        Assert.Equal("\"App\"", substituted.ToCode());
    }

    /// <summary>
    /// Only a <em>string</em> constant is re-spelled in the hole's place. A <see langword="bool"/>
    /// argument keeps the local's name, and the template carries no constant, so the element it decorates
    /// stays on the element path: the cost is a missed fold through a view part, never a wrong DOM.
    /// </summary>
    [Fact]
    public void Substitute_LoneHole_WithABooleanConstantKeepsTheLocalName()
    {
        var template = ExpressionTemplate.Create([new ParameterHoleExpressionSegment(0)]);

        var substituted = template.Substitute(
            [new SubstitutedArgument("__l0", new BooleanConstant(true))]);

        Assert.Null(substituted.Constant);
        Assert.Equal("__l0", substituted.ToCode());
    }

    [Fact]
    public void Substitute_LoneHole_WithoutAConstantKeepsTheLocalName()
    {
        var template = ExpressionTemplate.Create(
            [new ParameterHoleExpressionSegment(0)]);

        var substituted = template.Substitute([new SubstitutedArgument("item", Constant: null)]);

        Assert.Null(substituted.Constant);
        Assert.Equal("item", substituted.ToCode());
    }

    /// <summary>
    /// A hole surrounded by other text is not folded: recomputing the value would need expression
    /// evaluation, for example across an interpolation.
    /// </summary>
    [Fact]
    public void Substitute_HoleWithSurroundingText_CarriesNoConstant()
    {
        var template = ExpressionTemplate.Create(
        [
            new LiteralExpressionSegment("$\"Hello "),
            new ParameterHoleExpressionSegment(0),
            new LiteralExpressionSegment("\""),
        ]);

        var substituted = template.Substitute([new SubstitutedArgument("__l0", new StringConstant("App"))]);

        Assert.Null(substituted.Constant);
        Assert.Equal("$\"Hello __l0\"", substituted.ToCode());
    }

    [Fact]
    public void Equality_DistinguishesTemplatesThatDifferOnlyByConstant()
    {
        ImmutableArray<ExpressionSegment> segments = [new LiteralExpressionSegment("\"a\"")];

        Assert.NotEqual(
            ExpressionTemplate.Create(segments, new StringConstant("a")),
            ExpressionTemplate.Create(segments, constant: null));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void ForBooleanConstant_EmitsTheMatchingLiteral(bool value, string expectedCode)
    {
        var template = ExpressionTemplateFactory.ForBooleanConstant(value);

        Assert.Equal(new BooleanConstant(value), template.Constant);
        Assert.Equal(expectedCode, template.Substitute([]).ToCode());
    }

    /// <summary>
    /// An event's modifiers take part in its equality, and an unwritten modifier is distinct from a
    /// written <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// This is what stops the incremental generator handing back a cached result across a changed
    /// modifier: the templates are compared structurally, and nothing else in the pipeline re-reads the
    /// call site once a node has been cached. The null/false distinction is the one a reader is most
    /// likely to collapse, and it decides whether a call is emitted at all (#368).
    /// </remarks>
    [Fact]
    public void EventTemplate_ModifiersTakePartInEquality()
    {
        var handler = ExpressionTemplate.Literal("Handler");
        var written = ExpressionTemplateFactory.ForBooleanConstant(true);
        var writtenFalse = ExpressionTemplateFactory.ForBooleanConstant(false);

        // Named rather than positional, so a channel added to EventTemplate cannot shift these into each
        // other's places and leave the test still passing against the wrong pair.
        var trueModifier = new EventTemplate("onwheel", handler, PreventDefault: written);
        var falseModifier = new EventTemplate("onwheel", handler, PreventDefault: writtenFalse);
        var unwritten = new EventTemplate("onwheel", handler);

        // Written false is not unwritten. This is the distinction that decides whether a call is emitted
        // at all, and the one a reader is most likely to collapse.
        Assert.NotEqual(falseModifier, unwritten);

        // The two modifiers are separate channels: setting one must not read as setting the other.
        Assert.NotEqual(trueModifier, new EventTemplate("onwheel", handler, StopPropagation: written));
    }
}

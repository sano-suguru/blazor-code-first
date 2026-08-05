using System.Collections.Immutable;
using BlazorCodeFirst.Compiler;

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
            new ConstantInfo("a"));

        Assert.Equal(new ConstantInfo("a"), template.Constant);
    }

    [Fact]
    public void ConstantNullString_IsDistinctFromNotConstant()
    {
        var constantNull = ExpressionTemplate.Create(
            [new LiteralExpressionSegment("null")],
            new ConstantInfo(null));

        Assert.NotNull(constantNull.Constant);
        Assert.Null(constantNull.Constant!.Value.Text);
    }

    /// <summary>
    /// A template carrying a constant never contains a parameter hole: a hole is created only for an
    /// identifier bound to a composable parameter, and a parameter reference has no constant value. So
    /// substitution passes the constant through unchanged rather than having to recompute or clear it.
    /// </summary>
    [Fact]
    public void Substitute_PreservesTheConstant()
    {
        var template = ExpressionTemplate.Create(
            [new LiteralExpressionSegment("\"a\"")],
            new ConstantInfo("a"));

        var substituted = template.Substitute([new SubstitutedArgument("__local", Constant: null)]);

        Assert.Equal(new ConstantInfo("a"), substituted.Constant);
    }

    /// <summary>
    /// A template that is exactly one parameter hole takes the argument's constant, which is the shape a
    /// composable pass-through produces (Span[title], .Class(cls)). The substituted code becomes the
    /// constant literal rather than the local's name; the value is the same either way.
    /// </summary>
    [Fact]
    public void Substitute_LoneHole_TakesTheArgumentsConstant()
    {
        var template = ExpressionTemplate.Create(
            [new ParameterHoleExpressionSegment(0)]);

        var substituted = template.Substitute([new SubstitutedArgument("__l0", new ConstantInfo("App"))]);

        Assert.Equal(new ConstantInfo("App"), substituted.Constant);
        Assert.Equal("\"App\"", substituted.ToCode());
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

        var substituted = template.Substitute([new SubstitutedArgument("__l0", new ConstantInfo("App"))]);

        Assert.Null(substituted.Constant);
        Assert.Equal("$\"Hello __l0\"", substituted.ToCode());
    }

    [Fact]
    public void Equality_DistinguishesTemplatesThatDifferOnlyByConstant()
    {
        ImmutableArray<ExpressionSegment> segments = [new LiteralExpressionSegment("\"a\"")];

        Assert.NotEqual(
            ExpressionTemplate.Create(segments, new ConstantInfo("a")),
            ExpressionTemplate.Create(segments, constant: null));
    }
}

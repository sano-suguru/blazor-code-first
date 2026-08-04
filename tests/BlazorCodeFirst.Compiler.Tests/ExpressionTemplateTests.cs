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

        var result = template.Substitute(["__bcf_arg_1_0"]);

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

        var substituted = template.Substitute(["__local"]);

        Assert.Equal(new ConstantInfo("a"), substituted.Constant);
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

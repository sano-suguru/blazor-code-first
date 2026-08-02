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
}

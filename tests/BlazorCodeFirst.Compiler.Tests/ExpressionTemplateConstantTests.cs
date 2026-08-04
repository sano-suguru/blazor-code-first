using System.Collections.Immutable;
using System.Linq;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Checks that <see cref="Analysis.ExpressionTemplateFactory"/> captures compile-time constant values off
/// the semantic model. The value, not the source text, is what the fold needs: the child of
/// <c>H1["Benchmark"]</c> is stored as the literal source <c>"Benchmark"</c> with quotes included, which
/// the emitter cannot tell apart from a property reference.
/// </summary>
public sealed class ExpressionTemplateConstantTests
{
    /// <summary>Models the host source and returns the single component's root node.</summary>
    private static RenderNode RootOf(string body, string members = "")
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                {{members}}
                protected override View Body => {{body}};
            }
            """;

        var model = CompilationTestHost.ModelSingleComponent(source);
        return model.RootNode;
    }

    private static ExpressionTemplate TextOf(RenderNode node) =>
        ((TextContentNode)((ElementNode)node).Children.AsImmutableArray().Single()).Content;

    [Fact]
    public void StringLiteralChild_CarriesItsConstantValue()
    {
        var text = TextOf(RootOf("""H1["Benchmark"]"""));

        Assert.Equal("Benchmark", text.Constant?.Text);
    }

    [Fact]
    public void ConstFieldChild_CarriesItsConstantValue()
    {
        var text = TextOf(RootOf(
            "H1[Label]",
            members: "private const string Label = \"Benchmark\";"));

        Assert.Equal("Benchmark", text.Constant?.Text);
    }

    [Fact]
    public void ConstantConcatenationChild_CarriesTheFoldedValue()
    {
        var text = TextOf(RootOf("""H1["Bench" + "mark"]"""));

        Assert.Equal("Benchmark", text.Constant?.Text);
    }

    /// <summary>
    /// The correction recorded on #140: foldability is strictly narrower than the SSC classification.
    /// This expression is SSC (its sequence numbers are statically assignable) but its value is not a
    /// compile-time constant, so it must not carry one.
    /// </summary>
    [Fact]
    public void InterpolatedPropertyChild_CarriesNoConstant()
    {
        var text = TextOf(RootOf(
            """Span[$"Count: {Count}"]""",
            members: "private int Count => 7;"));

        Assert.Null(text.Constant);
    }

    [Fact]
    public void PropertyReferenceChild_CarriesNoConstant()
    {
        var text = TextOf(RootOf(
            "H1[Label]",
            members: "private string Label => \"Benchmark\";"));

        Assert.Null(text.Constant);
    }

    [Fact]
    public void ConstantAttributeValue_CarriesItsConstantValue()
    {
        var element = (ElementNode)RootOf("""A.Href("/home")["Home"]""");
        var href = element.Attributes.AsImmutableArray().Single(a => a.Name == "href");

        Assert.Equal("/home", href.Value.Constant?.Text);
    }

    [Fact]
    public void ConstantClassValue_CarriesItsConstantValue()
    {
        var element = (ElementNode)RootOf("""Div.Class("card")["x"]""");

        Assert.Equal("card", element.Classes.AsImmutableArray().Single().Constant?.Text);
    }
}

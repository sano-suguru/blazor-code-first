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

        Assert.Equal(new StringConstant("Benchmark"), text.Constant);
    }

    [Fact]
    public void ConstFieldChild_CarriesItsConstantValue()
    {
        var text = TextOf(RootOf(
            "H1[Label]",
            members: "private const string Label = \"Benchmark\";"));

        Assert.Equal(new StringConstant("Benchmark"), text.Constant);
    }

    [Fact]
    public void ConstantConcatenationChild_CarriesTheFoldedValue()
    {
        var text = TextOf(RootOf("""H1["Bench" + "mark"]"""));

        Assert.Equal(new StringConstant("Benchmark"), text.Constant);
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

        Assert.Equal(new StringConstant("/home"), href.Value.Constant);
    }

    [Fact]
    public void ConstantClassValue_CarriesItsConstantValue()
    {
        var element = (ElementNode)RootOf("""Div.Class("card")["x"]""");

        Assert.Equal(
            new StringConstant("card"), element.Classes.AsImmutableArray().Single().Constant);
    }

    /// <summary>
    /// A constant <see langword="null"/> is its own state, not the absence of a constant: the expression
    /// is side-effect free (so a composable local bound to it may be dropped) and <c>AddAttribute</c>
    /// omits the attribute, which is exactly what the fold writes for it.
    /// </summary>
    [Fact]
    public void ConstantNullAttributeValue_CarriesTheNullConstant()
    {
        var element = (ElementNode)RootOf(
            """Div.Attr("id", Missing)["x"]""",
            members: "private const string? Missing = null;");
        var id = element.Attributes.AsImmutableArray().Single(a => a.Name == "id");

        Assert.Equal(new NullConstant(), id.Value.Constant);
    }

    /// <summary>
    /// The <see langword="bool"/> overload's value is classified as a <see langword="bool"/> and not as a
    /// constant with nothing to write, which is what lets the fold express both of its outcomes.
    /// </summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void BooleanAttributeValue_CarriesTheBooleanConstant(string literal, bool expected)
    {
        var element = (ElementNode)RootOf($"""Input.Attr("disabled", {literal})""");
        var disabled = element.Attributes.AsImmutableArray().Single(a => a.Name == "disabled");

        Assert.Equal(new BooleanConstant(expected), disabled.Value.Constant);
    }

    /// <summary>
    /// The distinction #158 turns on: a constant of a type other than <see langword="string"/> or
    /// <see langword="bool"/> must not be read as a constant <see langword="null"/>. Both used to arrive
    /// as <c>{ Text: null }</c>, and the fold reads that state as "the attribute is omitted", which is
    /// right for a <see langword="null"/> and drops an attribute that renders for everything else.
    /// </summary>
    /// <remarks>
    /// Measured through the component parameter channel because it is the only one that carries a
    /// non-string, non-<see langword="bool"/> value today: <c>.Attr</c> takes a <see langword="string"/>
    /// or a <see langword="bool"/>, deliberately (#158), so no attribute value can be an <c>int</c>.
    /// </remarks>
    [Fact]
    public void NonStringConstantValue_IsDistinctFromAConstantNull()
    {
        var component = (ComponentNode)RootOf(
            "Component<Card>().Param(c => c.Count, 3)",
            members: """
                public sealed class Card : Microsoft.AspNetCore.Components.ComponentBase
                {
                    [Microsoft.AspNetCore.Components.Parameter] public int Count { get; set; }
                }
                """);
        var count = component.Parameters.AsImmutableArray().Single();

        Assert.Equal(new RuntimeFormattedConstant(), count.Value.Constant);
    }
}

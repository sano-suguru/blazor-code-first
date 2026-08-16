namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ForEachTransplantTests
{
    /// <summary>A component whose <c>ForEach</c> content is <c>$CONTENT$</c>.</summary>
    private const string Host = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body => Html.ForEach(_items, x => x, $CONTENT$);
        }
        """;

    private const string BlockContent = """
        x =>
                {
                    var label = x.ToUpperInvariant();
                    return Html.Span[label];
                }
        """;

    private const string ExpressionContent = "x => Html.Span[x.ToUpperInvariant()]";

    private static GeneratorRunResult Run(string content) =>
        CompilationTestHost.RunGenerator(Host.Replace("$CONTENT$", content));

    [Fact]
    public void ForEachContent_WhenBlockBodiedWithOneTrailingReturn_TransplantsTheStatements()
    {
        var result = Run(BlockContent);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3004");

        // The statement is transplanted with the iteration variable substituted for the lambda parameter,
        // and `var` resolved the way every other transplanted type reference is: the generated file
        // carries no using directives.
        Assert.Contains("string label = __bcf_item_0.ToUpperInvariant();", generated);

        // The key still lands on the content root, past the statements.
        Assert.Contains("__builder.SetKey(__bcf_item_0);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenBlockDeconstructs_KeepsTheVarThatDesignationRequires()
    {
        // The other position the deconstruction was measured in (#342). The iteration variable is
        // substituted as it is in any other transplanted statement; what changes is the type ahead of the
        // designation list, which stays `var` because no other type can be written there.
        var result = Run("""
            x =>
                    {
                        var (a, b) = (x, x);
                        return Html.Span[a + b];
                    }
            """);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("var (a, b) = (__bcf_item_0, __bcf_item_0);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenBlockBodiedWithOneTrailingReturn_KeepsTheContentSequenceWidth()
    {
        // Statements emit no sequence-consuming call, so the block form must allocate the same numbers
        // the expression form does.
        Assert.Equal(
            SequenceArguments.InTextOrder(
                Assert.Single(Run(ExpressionContent).GeneratedSources).SourceText.ToString()),
            SequenceArguments.InTextOrder(
                Assert.Single(Run(BlockContent).GeneratedSources).SourceText.ToString()));
    }

    [Theory]
    // Two returns: each would need a sequence space of its own, which is the wider Transplantable slice.
    [InlineData(
        "two returns",
        """
        x =>
                {
                    if (x.Length == 0)
                        return Html.Span["empty"];

                    return Html.Span[x];
                }
        """)]
    // A local spelled with the generator's reserved prefix, which the rename plan cannot carry across the
    // several templates a block becomes.
    [InlineData(
        "generator-reserved local name",
        """
        x =>
                {
                    var __bcf_item_0 = x.ToUpperInvariant();
                    return Html.Span[__bcf_item_0];
                }
        """)]
    // The builder the transplanted statements are written beside. Refused from the same reader that
    // refuses it in a design-time expression getter, so both positions hold the reserved set alike.
    [InlineData(
        "the builder's name",
        """
        x =>
                {
                    var __builder = x.ToUpperInvariant();
                    return Html.Span[__builder];
                }
        """)]
    // The same name bound by a designation. Inside the generated loop it shadowed the builder the frames
    // around it are written against, and nothing reported it (#348).
    [InlineData(
        "the builder's name through a designation",
        """
        x =>
                {
                    int.TryParse(x, out var __builder);
                    return Html.Span[x];
                }
        """)]
    public void ForEachContent_WhenBlockIsOutsideTheAcceptedShape_ReportsBCF3004(
        string shape, string content)
    {
        var result = Run(content);

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF3004"),
            $"{shape}: expected BCF3004, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }

    [Fact]
    public void ForEachContent_WhenAnExpressionBodiedLambdaDeclaresTheBuildersName_ReportsBCF3004()
    {
        // An expression-bodied content lambda is transplanted into the generated loop with the author's
        // names kept, exactly as a block-bodied one is, so it holds the same reserved set. The scan reached
        // only the block arm, and this shape emitted the collision instead (#389).
        var result = Run("""x => Html.Span[x is string __builder ? __builder : "x"]""");

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF3004"),
            $"expected BCF3004, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }
}

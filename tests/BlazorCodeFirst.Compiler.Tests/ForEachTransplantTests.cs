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
        // carries no using directives. The authored local is named by the block's own preorder ordinal --
        // 1, since the ForEach it belongs to took 0 -- so two expansions cannot declare one name (#336).
        Assert.Contains("string __bcf_local_1_0 = __bcf_item_0.ToUpperInvariant();", generated);

        // The key still lands on the content root, past the statements.
        Assert.Contains("__builder.SetKey(__bcf_item_0);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenANestedBlockDeclaresTheEnclosingBlocksName_GeneratesCompilingCode()
    {
        // #336's second measured shape: the part expands inside the caller's own ForEach content block,
        // so its statements land inside a scope that already declares the name. This one is a real
        // nesting rather than a flattening, and the mint answers both.
        const string source = """
            using BlazorCodeFirst;
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                private static readonly List<string> Outer = new() { "a" };
                private static readonly List<string> Inner = new() { "b" };

                protected override View Body => ForEach(Outer, x => x, x =>
                {
                    var label = x.ToUpperInvariant();
                    return Div[Span[label], Part()];
                });

                [ViewPart]
                private static View Part() => ForEach(Inner, y => y, y =>
                {
                    var label = y.ToUpperInvariant();
                    return Span[label];
                });
            }
            """;

        CompilationTestHost.AssertOutputCompiles(CompilationTestHost.RunGenerator(source));
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
    public void ForEachContent_WhenBlockIsOutsideTheAcceptedShape_ReportsBCF3004(
        string shape, string content)
    {
        var result = Run(content);

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF3004"),
            $"{shape}: expected BCF3004, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }
}

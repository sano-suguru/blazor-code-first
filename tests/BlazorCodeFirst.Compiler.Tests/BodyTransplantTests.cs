using System.Linq;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The design-time expression getter on the Transplantable path (ARCHITECTURE.md §2.3): the same block
/// shape <c>ForEach</c>'s content accepts, read from <c>Body</c> and <c>Chrome</c>.
/// </summary>
public sealed class BodyTransplantTests
{
    /// <summary>A component whose <c>Body</c> getter is <c>$GETTER$</c>.</summary>
    private const string Host = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private string Title => "t";

            protected override View Body
            $GETTER$
        }
        """;

    private const string BlockGetter = """
        {
                get
                {
                    var label = Title;
                    return Span[label];
                }
            }
        """;

    private const string ExpressionGetter = """
        {
                get
                {
                    return Span[Title];
                }
            }
        """;

    private static GeneratorRunResult Run(string getter) =>
        CompilationTestHost.RunGenerator(Host.Replace("$GETTER$", getter));

    [Fact]
    public void Body_WhenGetterIsBlockBodiedWithOneTrailingReturn_TransplantsTheStatements()
    {
        var result = Run(BlockGetter);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");

        // `var` is resolved the way every other transplanted type reference is: the generated file
        // carries no using directives, so the written type has to stand on its own.
        Assert.Contains("string label = Title;", generated);

        // The statements land ahead of the frames, which is what makes the local readable from them.
        Assert.True(
            generated.IndexOf("string label = Title;", System.StringComparison.Ordinal)
                < generated.IndexOf("__builder.OpenElement(0,", System.StringComparison.Ordinal),
            "The transplanted statements must precede the frame emission that reads them.");

        Assert.Contains("__builder.AddContent(1, label);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
        CompilationTestHost.AssertGeneratedOutputHasNoWarnings(result);
    }

    [Fact]
    public void Body_WhenGetterIsBlockBodiedWithOneTrailingReturn_KeepsTheSequenceWidth()
    {
        // Statements emit no sequence-consuming call, so the block form must allocate the same numbers
        // the single-expression form does.
        Assert.Equal(
            SequenceArguments.InTextOrder(
                Assert.Single(Run(ExpressionGetter).GeneratedSources).SourceText.ToString()),
            SequenceArguments.InTextOrder(
                Assert.Single(Run(BlockGetter).GeneratedSources).SourceText.ToString()));
    }

    [Fact]
    public void Chrome_WhenGetterIsBlockBodiedWithOneTrailingReturn_TransplantsTheStatements()
    {
        // Chrome is the same elected design-time expression, so it takes the shape with Body rather than
        // by a rule of its own.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Shell : ChromeLayoutBase
            {
                protected override View Chrome
                {
                    get
                    {
                        var shell = "shell";
                        return Main.Class(shell)[Body];
                    }
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");
        Assert.Contains("string shell = \"shell\";", generated);
        Assert.Contains("__builder.AddContent(2, Body);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenTransplantedStatementsMutateState_LeavesTheRefusalToBCF3001()
    {
        // Admitting statements does not admit side effects, it changes which diagnostic refuses them. A
        // design-time expression may not mutate state (CONTRIBUTING.md §Conventions the code must uphold);
        // BCF1004 used to refuse this getter before the mutation was the point, and BCF3001 is now the only
        // diagnostic standing on the shape. RenderMutationAnalyzerTests
        // .RenderMutationAnalyzer_MutationInBlockBodiedGetter_ReportsBCF3001 holds that half on the same
        // source, so the analyzer is not run twice here.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                private int _n;

                protected override View Body
                {
                    get
                    {
                        _n++;
                        return Span["x"];
                    }
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1004");
        Assert.Contains("_n++;", Assert.Single(result.GeneratedSources).SourceText.ToString());
    }

    [Theory]
    // Two returns: each needs a sequence space of its own, which is the wider Transplantable slice.
    [InlineData(
        "two returns",
        """
        {
                get
                {
                    if (Title.Length == 0)
                        return Span["empty"];

                    return Span[Title];
                }
            }
        """)]
    // Native control flow, for the same reason.
    [InlineData(
        "native foreach",
        """
        {
                get
                {
                    foreach (var c in Title)
                        System.Console.WriteLine(c);

                    return Span[Title];
                }
            }
        """)]
    // A local spelled with the generator's reserved prefix, which the rename plan cannot carry across the
    // several templates a block becomes.
    [InlineData(
        "generator-reserved local name",
        """
        {
                get
                {
                    var __bcf_label = Title;
                    return Span[__bcf_label];
                }
            }
        """)]
    // The builder the generated frames are written against. A getter's statements share its scope.
    [InlineData(
        "the builder's name",
        """
        {
                get
                {
                    var __builder = Title;
                    return Span[__builder];
                }
            }
        """)]
    public void Body_WhenGetterIsOutsideTheAcceptedShape_StaysBCF1004(string shape, string getter)
    {
        var result = Run(getter);

        Assert.Empty(result.GeneratedSources);
        // BCF1003 blames an expression and BCF1004 the declaration around it; the author gets one fix.
        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF1004")
                && !result.Diagnostics.Any(d => d.Id == "BCF1003"),
            $"{shape}: expected BCF1004 alone, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }
}

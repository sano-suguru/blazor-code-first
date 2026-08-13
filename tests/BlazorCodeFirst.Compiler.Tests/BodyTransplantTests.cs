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
        // carries no using directives, so the written type has to stand on its own. The name is the
        // block's, minted from its preorder ordinal, because the statements of two expansions land in one
        // scope and the author's own name would collide there (#336).
        Assert.Contains("string __bcf_local_0_0 = Title;", generated);

        // The statements land ahead of the frames, which is what makes the local readable from them.
        Assert.True(
            generated.IndexOf("string __bcf_local_0_0 = Title;", System.StringComparison.Ordinal)
                < generated.IndexOf("__builder.OpenElement(0,", System.StringComparison.Ordinal),
            "The transplanted statements must precede the frame emission that reads them.");

        // The declaration and the reference take the one substitution, so they cannot disagree.
        Assert.Contains("__builder.AddContent(1, __bcf_local_0_0);", generated);
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
        Assert.Contains("string __bcf_local_0_0 = \"shell\";", generated);
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

    [Fact]
    public void Body_WhenGetterBlockAndExpandedPartBlockDeclareTheSameName_GeneratesCompilingCode()
    {
        // #336: expansion flattens the part's block into the getter's own, so the two `label`
        // declarations land in one scope. Nothing about that is nested in what the author wrote, and
        // the CS0136 it produced pointed into a file the author does not write.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                private static readonly string[] Outer = ["a"];
                private static readonly string[] Inner = ["b"];

                protected override View Body
                {
                    get
                    {
                        var label = Outer[0];
                        return Div[Span[label], Part()];
                    }
                }

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

    /// <summary>
    /// A component whose getter block and whose expanded part's block both write <c>$DECLARATION$</c>,
    /// reading it back through <c>$READ$</c>. The part's block lands inside the getter's own scope, so a
    /// name left as written is declared in an enclosing scope and its own — the #336 shape.
    /// </summary>
    private const string NestedBlocksHost = """
        using BlazorCodeFirst;
        using System.Collections.Generic;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private static readonly List<string> Items = new() { "a" };

            protected override View Body
            {
                get
                {
                    var x = Items[0];
                    $DECLARATION$
                    return Div[Span[$READ$], Part()];
                }
            }

            [ViewPart]
            private static View Part() => ForEach(Items, x => x, x =>
            {
                $DECLARATION$
                return Span[$READ$];
            });
        }
        """;

    [Theory]
    // Two declarators in one statement, and a second that reads the first.
    [InlineData("var a = \"p\"; var b = a + x;", "a + b")]
    // A designation bound by an expression statement rather than by a declaration.
    [InlineData("int.TryParse(x, out var n);", "n.ToString()")]
    // A pattern designation, which binds in the block's scope as a declarator does, and so lands twice
    // in the flattened one however narrowly the author reads it.
    [InlineData("var upper = x is { Length: > 0 } s ? s.ToUpperInvariant() : x;", "upper")]
    public void Body_WhenNestedBlocksDeclareTheSameName_MintsEachDeclarationsName(
        string declaration, string read)
    {
        // #336: the mint has to cover every way a leading statement binds a name, not the single simple
        // declarator alone. A shape left as written is declared in an enclosing scope and again in the
        // expanded one, which is CS0136 and not a cosmetic difference.
        var result = CompilationTestHost.RunGenerator(
            NestedBlocksHost.Replace("$DECLARATION$", declaration).Replace("$READ$", read));

        Assert.Empty(result.Diagnostics);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenALeadingStatementWritesALambda_LeavesItsParameterAsWritten()
    {
        // The mint covers what the block's scope binds. A lambda parameter is bound in the lambda's own
        // scope, is readable only there, and so cannot collide with anything an expansion brings.
        var result = CompilationTestHost.RunGenerator(
            NestedBlocksHost
                .Replace("$DECLARATION$", "var f = (string n) => n + x;")
                .Replace("$READ$", "f(x)"));

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        CompilationTestHost.AssertOutputCompiles(result);
        Assert.Contains("(string n) => n +", generated);
        Assert.DoesNotContain("var f =", generated);
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

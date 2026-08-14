using System.Linq;
using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The <c>[ViewPart]</c> position on the Transplantable path (ARCHITECTURE.md §2.3): the block shape the
/// design-time expression getter and a <c>ForEach</c> content lambda already accept, plus the naming that
/// lets a block survive being expanded more than once (#336).
/// </summary>
public sealed class ViewPartTransplantTests
{
    /// <summary>
    /// A component whose <c>Body</c> is <c>$BODY$</c> and whose one part is <c>$PART$</c>, both written as
    /// whole members so a test can vary either without restating the class around it.
    /// </summary>
    private const string Host = """
        using System.Collections.Generic;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private static readonly List<string> Inner = new() { "b" };
            private readonly List<string> _outer = new() { "a" };

            protected override View Body $BODY$

            [ViewPart]
            private static View $PART$
        }
        """;

    private const string ExpressionBodiedPart = "Part(string title) => Span[title.ToUpperInvariant()];";

    private const string StatementBodiedPart = """
        Part(string title)
            {
                var label = title.ToUpperInvariant();
                return Span[label];
            }
        """;

    /// <summary>A part whose statements sit in a <c>ForEach</c> content block, the pre-#315 shape.</summary>
    private const string LoopingPart = """
        Part() => ForEach(Inner, y => y, y =>
            {
                var label = y.ToUpperInvariant();
                return Span[label];
            });
        """;

    private static GeneratorRunResult Run(string body, string part) =>
        CompilationTestHost.RunGenerator(Host.Replace("$BODY$", body).Replace("$PART$", part));

    [Fact]
    public void ViewPart_WhenBlockBodiedWithOneTrailingReturn_ExpandsWithItsStatements()
    {
        var result = Run("""=> Div[Part("one")];""", StatementBodiedPart);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");

        // The argument local the expansion binds, then the author's statement reading it under the name
        // expansion minted for this call.
        Assert.Contains("string __bcf_arg_1_0 = \"one\";", generated);
        Assert.Contains("string __bcf_local_2_0 = __bcf_arg_1_0.ToUpperInvariant();", generated);
        Assert.Contains("__builder.AddContent(2, __bcf_local_2_0);", generated);
        CompilationTestHost.AssertOutputCompiles(result);
        CompilationTestHost.AssertGeneratedOutputHasNoWarnings(result);
    }

    [Fact]
    public void ViewPart_WhenBlockBodiedPartIsCalledTwice_NamesEachExpansionsLocalApart()
    {
        // The collision the naming exists for: one written local, two expansions, one generated scope.
        var result = Run("""=> Div[Part("one"), Part("two")];""", StatementBodiedPart);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("string __bcf_local_2_0 = __bcf_arg_1_0.ToUpperInvariant();", generated);
        Assert.Contains("string __bcf_local_6_0 = __bcf_arg_5_0.ToUpperInvariant();", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewPart_WhenBlockBodiedPartIsExpandedBesideAnAuthoredLocal_KeepsTheComponentsOwnName()
    {
        // #336's first shape. The getter's local is written once into RenderView and cannot meet itself, so
        // it stays as the author spelled it; only the expanded body, which can arrive twice, is renamed.
        var result = Run(
            """
            {
                    get
                    {
                        var label = _outer[0];
                        return Div[Span[label], Part()];
                    }
                }
            """,
            LoopingPart);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("string label = _outer[0];", generated);
        Assert.Contains("string __bcf_local_6_0 = __bcf_item_5.ToUpperInvariant();", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewPart_WhenExpandedInsideTheCallersOwnBlock_DoesNotNestTwoAuthoredLocals()
    {
        // #336's second shape, and the one reachable before the [ViewPart] position accepted statements at
        // all: EmitExpansion opens no brace, so the part's block lands inside the caller's loop body.
        var result = Run(
            """
            => ForEach(_outer, x => x, x =>
                {
                    var label = x.ToUpperInvariant();
                    return Div[Span[label], Part()];
                });
            """,
            LoopingPart);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("string label = __bcf_item_0.ToUpperInvariant();", generated);
        Assert.Contains("string __bcf_local_7_0 = __bcf_item_6.ToUpperInvariant();", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewPart_WhenBlockBodied_KeepsTheSequenceWidth()
    {
        // Statements consume no sequence argument, so the two body forms of one part must allocate the
        // same numbers at the same call site.
        const string body = """=> Div[Part("one")];""";

        Assert.Equal(
            SequenceArguments.InTextOrder(
                Assert.Single(Run(body, ExpressionBodiedPart).GeneratedSources).SourceText.ToString()),
            SequenceArguments.InTextOrder(
                Assert.Single(Run(body, StatementBodiedPart).GeneratedSources).SourceText.ToString()));
    }

    [Theory]
    // A second return: each needs a sequence space of its own, which is the wider Transplantable slice.
    [InlineData(
        "two returns",
        """
        Part(string title)
            {
                if (title.Length == 0)
                    return Span["empty"];

                return Span[title];
            }
        """)]
    // Native control flow, refused in this position for the reason it is refused in the other two.
    [InlineData(
        "native foreach",
        """
        Part(string title)
            {
                foreach (var c in title)
                {
                }

                return Span[title];
            }
        """)]
    // The builder the transplanted statements are written beside.
    [InlineData(
        "the builder's name",
        """
        Part(string title)
            {
                var __builder = title.ToUpperInvariant();
                return Span[__builder];
            }
        """)]
    public void ViewPart_WhenBodyIsOutsideTheAcceptedShape_ReportsBCF1002(string shape, string part)
    {
        var result = Run("""=> Div[Part("one")];""", part);

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF1002"),
            $"{shape}: expected BCF1002, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }

    [Fact]
    public void ViewPart_WhenBlockBodiedPartCallsAnother_AsKeyedForEachContent_KeysPastBothBlocks()
    {
        // Two blocks stacked in one expansion, under a key. The key attaches to the content root, which
        // sits past both sets of statements, and the inner block's names are minted at its own call's
        // ordinal rather than the outer one's. Its own class: two parts is a shape Host does not spell.
        var result = CompilationTestHost.RunGenerator("""
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                private readonly List<string> _outer = new() { "a" };

                protected override View Body => ForEach(_outer, x => x, x => Outer(x));

                [ViewPart]
                private static View Outer(string title)
                {
                    var upper = title.ToUpperInvariant();
                    return Div[Inner(upper)];
                }

                [ViewPart]
                private static View Inner(string label)
                {
                    var trimmed = label.Trim();
                    return Span[trimmed];
                }
            }
            """);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF3003");

        // Every transplanted local carries a name of its own, and the key still reaches the root frame.
        Assert.Contains("__builder.SetKey(__bcf_item_0);", generated);
        Assert.Equal(2, Regex.Count(generated, "__bcf_local_[0-9]+_0 ="));
        CompilationTestHost.AssertOutputCompiles(result);
        CompilationTestHost.AssertGeneratedOutputHasNoWarnings(result);
    }

    [Fact]
    public void ViewPart_WhenSlotIsNamedInALeadingStatement_CountsItTowardsBCF3025()
    {
        // The slot count reads the whole accepted body. A Slot named in a leading statement is written into
        // the expansion just as one in the returned expression is, so counting only the expression would
        // let this pass as named once and place the caller's content twice. Its own class: a SlotView part
        // called with brackets is a shape Host does not spell.
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Div[Part()["x"]];

                [ViewPart]
                private static SlotView Part()
                {
                    var held = Slot;
                    return Div[Slot];
                }
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3025");
    }
}

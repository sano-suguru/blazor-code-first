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
        using System.Linq;
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

    private const string IfElsePart = """
        Part(bool flag)
            {
                if (flag)
                {
                    return Span["yes"];
                }
                else
                {
                    return Span["no"];
                }
            }
        """;

    private const string LeadingLocalIfElsePart = """
        Part(bool flag)
            {
                var label = flag ? "yes" : "no";
                if (flag)
                {
                    return Span[label];
                }
                else
                {
                    return Span[label];
                }
            }
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

    [Theory]
    // Two declarators in one statement, the second reading the first.
    [InlineData("var a = title; var b = a + title;", "a + b")]
    // Deconstruction binds through designations and writes no declarator at all, so both names come from
    // the mint. Its `var` is the type reference the qualification leaves as written (#342), which is what
    // lets this row reach the mint at all.
    [InlineData("var (a, b) = (title, title);", "a + b")]
    // A designation bound by an expression statement rather than by a declaration.
    [InlineData("int.TryParse(title, out var n);", "n.ToString()")]
    // A pattern designation, which binds in the block's scope as a declarator does.
    [InlineData("var upper = title is { Length: > 0 } s ? s.ToUpperInvariant() : title;", "upper")]
    public void ViewPart_WhenACalledTwicePartDeclaresThroughAnyShape_NamesBothExpansionsApart(
        string declaration, string read)
    {
        // The mint has to cover every way a leading statement binds a name, and which ways those are is one
        // list read by the registration and by the splice that replaces the identifier
        // (ExpressionTemplateFactory.TryGetDeclaredLocalIdentifier). A shape dropped from it leaves the
        // declaration under the author's name in both expansions, which is the CS0136 #336 closed.
        var result = Run(
            """=> Div[Part("one"), Part("two")];""",
            $$"""
            Part(string title)
                {
                    {{declaration}}
                    return Span[{{read}}];
                }
            """);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Theory]
    // A pattern designation in a child value, which binds into the scope the frames are written in.
    [InlineData("""Span[Inner[0] is { Length: > 0 } ok ? ok : "n"]""")]
    // An `out var` in an argument, the other form that leaks a name out of the expression it is written in.
    [InlineData("""Span[int.TryParse(Inner[0], out var n) ? n.ToString() : "0"]""")]
    // An attribute value rather than a child, so the widening is held to every expression channel the
    // normalization runs over and not only to the one the collision was measured in.
    [InlineData("""Span.Attr("title", Inner[0] is { Length: > 0 } ok ? ok : "n")["x"]""")]
    public void ViewPart_WhenACalledTwicePartDeclaresInsideItsExpression_NamesBothExpansionsApart(
        string expression)
    {
        // No block is involved: the part is one expression and so is the caller's Body. What the mint has
        // to cover is every declaration the expression binds into its enclosing scope, not only the ones a
        // leading statement writes, because expansion copies the expression to each call site (#343).
        var result = Run("""=> Div[Part(), Part()];""", $"Part() => {expression};");

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewPart_WhenACalledTwicePartDeclaresInsideItsForEachContent_MintsBesideTheIterationVariable()
    {
        // A content lambda is analyzed against a scope of its own, so its declarations are registered by
        // that recursion rather than by the body's walk, which stops at the lambda. Both mints then sit in
        // one ordinal space: the ForEach pushes its iteration variable first and the content's designation
        // after it, and expansion appends the names in that same order.
        var result = Run(
            """=> Div[Part(), Part()];""",
            """Part() => ForEach(Inner, y => y, y => Span[y is { Length: > 0 } ok ? ok : "n"]);""");

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // One name per expansion, and the iteration variable each reads is still its own loop's.
        Assert.Equal(
            2,
            Regex.Matches(generated, "__bcf_local_[0-9]+_0").Select(m => m.Value).Distinct().Count());
        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewPart_WhenTwoSiblingForEachContentsEachDeclareALocal_PopsTheFirstBeforePushingTheSecond()
    {
        // Both content lambdas are analyzed against the one ViewPartBodyContext this expansion carries, in
        // written order: the first content's Analyze has to pop its own render variable in its finally
        // before the second content's Push computes its ordinal from context.RenderVariableDepth, or every
        // ordinal from there on inherits the leak.
        var result = Run(
            """=> Div[Part()];""",
            """
            Part() => Div[
                    ForEach(Inner, x => x, x =>
                        {
                            var b = x.ToUpperInvariant();
                            return Span[b];
                        }),
                    ForEach(Inner, y => y, y =>
                        {
                            var c = y.ToUpperInvariant();
                            return Span[c];
                        })
                ];
            """);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);

        // Two distinct locals, each reading only its own loop's iteration variable — a leaked pop shifts
        // the second content's ordinal into the first's, which either collides two locals under one name
        // or reads past the substitution's end (both fail this count).
        Assert.Equal(
            2,
            Regex.Matches(generated, "__bcf_local_[0-9]+_0").Select(m => m.Value).Distinct().Count());
        Assert.Contains("string __bcf_local_4_0 = __bcf_item_3.ToUpperInvariant();", generated);
        Assert.Contains("string __bcf_local_8_0 = __bcf_item_7.ToUpperInvariant();", generated);
    }

    /// <summary>
    /// The item variable itself, not a locally-declared one, is the render variable under test here: each
    /// sibling loop's own <c>ForEach</c> pushes and pops it independently, so a leaked pop in the first
    /// loop's finally leaves the second loop's item at an elevated ordinal the part's own substitution
    /// array, sized from the correctly-scoped depth, was never built wide enough to hold (#487).
    /// </summary>
    [Fact]
    public void ViewPart_WhenTwoSiblingForEachContentsEachReadTheirOwnItem_PopsTheFirstBeforePushingTheSecond()
    {
        var result = Run(
            """=> Div[Part()];""",
            """
            Part() => Div[
                    ForEach(Inner, x => x, x => Span[x.ToUpperInvariant()]),
                    ForEach(Inner, y => y, y => Span[y.ToUpperInvariant()])
                ];
            """);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>Same leak, on <see cref="AnalyzeSplice"/>'s own push/pop pair rather than <c>ClassifyForEach</c>'s.</summary>
    [Fact]
    public void ViewPart_WhenTwoSiblingSpliceProjectionsEachReadTheirOwnItem_PopsTheFirstBeforePushingTheSecond()
    {
        var result = Run(
            """=> Div[Part()];""",
            """
            Part() => Div[[
                    ..Inner.Select(x => Span[x.ToUpperInvariant()]),
                    ..Inner.Select(y => Span[y.ToUpperInvariant()])
                ]];
            """);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewPart_WhenACalledTwicePartDeclaresInBothAStatementAndItsExpression_NamesThemInWrittenOrder()
    {
        // The two sources of minted names in one body. The ordinals are assigned in written order —
        // statements first, then the expression — and expansion appends the names in that same order, so
        // this pins the correspondence rather than only that the output compiles.
        var result = Run(
            """=> Div[Part("one"), Part("two")];""",
            """
            Part(string title)
                {
                    var upper = title.ToUpperInvariant();
                    return Span[upper is { Length: > 0 } ok ? ok : "n"];
                }
            """);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains(
            "__bcf_local_2_0 is { Length: > 0 } __bcf_local_2_1 ? __bcf_local_2_1 : \"n\"", generated);
        Assert.Contains(
            "__bcf_local_6_0 is { Length: > 0 } __bcf_local_6_1 ? __bcf_local_6_1 : \"n\"", generated);
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

    [Fact]
    public void ViewPart_WhenBodyEndsInNativeIfElse_ExpandsWithoutBcf1002()
    {
        var result = Run("""=> Div[Part(true)];""", IfElsePart);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");
        Assert.DoesNotContain("FragmentOf", generated);

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewPart_WhenCalledTwiceWithLeadingLocalBeforeNativeIf_RenamesEachExpansionIndependently()
    {
        var result = Run("""=> Fragment(Part(true), Part(false));""", LeadingLocalIfElsePart);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        CompilationTestHost.AssertOutputCompiles(result);

        // Both expansions' minted local names must differ -- confirms ExpandNode's existing per-call-site
        // ordinal-based renaming (ViewPartExpander.cs) reaches the leading statements of a
        // TransplantedIfNode-wrapped body the same way it already does for a plain trailing
        // return.
        var mintedNames = Regex.Matches(generated, "__bcf_local_\\d+_0")
            .Select(m => m.Value)
            .Distinct()
            .ToArray();
        Assert.Equal(2, mintedNames.Length);
    }
}

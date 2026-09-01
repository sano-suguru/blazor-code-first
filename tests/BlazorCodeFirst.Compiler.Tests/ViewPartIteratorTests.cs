using System.Globalization;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The <c>[ViewPart]</c> iterator shape (ARCHITECTURE.md §5.3, extended to the one shape `if`/`switch`
/// cannot cover -- <see cref="RenderExpressionAnalyzer.TryReadIteratorForEach"/>'s remarks): declaration-
/// only acceptance and rejection through <c>ViewPartDefinitionFactory</c>. No call site splices an
/// iterator part in yet (a later task wires that up), so a declaration standing alone with nothing calling
/// it is exactly what these tests can observe -- BCF1002 present or absent at the declaration, not that a
/// loop actually expands.
/// </summary>
public sealed class ViewPartIteratorTests
{
    /// <summary>A component whose one part is <c>$PART$</c>, standing alone -- nothing calls it.</summary>
    private const string Host = """
        using System.Collections.Generic;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Span["Body"];

            [ViewPart]
            private static IEnumerable<View> $PART$
        }
        """;

    private static GeneratorRunResult Run(string part) =>
        CompilationTestHost.RunGenerator(Host.Replace("$PART$", part));

    /// <summary>
    /// The id alone would read green for any other BCF1002 the same source could earn, so the message is
    /// checked too: every rejection here falls through <c>TryReadIteratorForEach</c> without matching any
    /// accepted body shape, so <c>ValidateDeclaration</c>'s one shared "unaccepted body" message is what
    /// each of these tests actually pins.
    /// </summary>
    private static void AssertReportsBcf1002(string part)
    {
        var result = Run(part);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            "must reach one return, or one foreach yielding one child per iteration",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static void AssertDoesNotReportBcf1002(string part)
    {
        var result = Run(part);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1002");

        // Unlike the native `if`/`switch` overloads, the iterator `Analyze` overload never reports
        // BCF2002: an iterator `[ViewPart]` has no call-site combinator spelling to have preferred instead
        // (#316).
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF2002");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IteratorViewPart_WhenDeclaredAlone_DoesNotReportBcf1002()
    {
        AssertDoesNotReportBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                    {
                        yield return Span[item];
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheForEachIsNotTheLastStatement_ReportsBcf1002()
    {
        // The outer block's last statement must be the `foreach` itself. Mutation: condition 1 in
        // TryReadIteratorForEach (`block.Statements[count - 1] is not ForEachStatementSyntax`).
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                    {
                        yield return Span[item];
                    }
                    yield return Span["trailing"];
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheBodyHasTwoYieldReturns_ReportsBcf1002()
    {
        // A second `yield return` inside the loop body is not the trailing one, so it is read as a
        // leading loop-body statement -- and a YieldStatementSyntax is neither a local declaration nor an
        // expression statement. Mutation: condition 5's TryReadLeadingStatements call over the loop body.
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                    {
                        yield return Span[item];
                        yield return Span[item];
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenAYieldReturnSitsOutsideTheLoop_ReportsBcf1002()
    {
        // A `yield return` ahead of the `foreach`, as one of the outer block's own leading statements, is
        // neither a local declaration nor an expression statement. Mutation: condition 6's
        // TryReadLeadingStatements call over the outer block.
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    yield return Span["before"];
                    foreach (var item in items)
                    {
                        yield return Span[item];
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheLoopBodyHasAYieldBreak_ReportsBcf1002()
    {
        // `yield break` is a YieldStatementSyntax too, but carries YieldBreakStatement's RawKind and a
        // null Expression -- rejected by the same pattern that requires YieldReturnStatement with a
        // non-null Expression. Mutation: condition 4's pattern match.
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                    {
                        yield break;
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheLoopBodyHasABreak_ReportsBcf1002()
    {
        // Mutation: condition 4's pattern match (the loop body's last statement must be a
        // YieldStatementSyntax).
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                    {
                        break;
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheLoopBodyHasAContinue_ReportsBcf1002()
    {
        // Mutation: condition 4's pattern match.
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                    {
                        continue;
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheLoopNestsASecondForEach_ReportsBcf1002()
    {
        // The nested `foreach` is the loop body's last statement, but it is a ForEachStatementSyntax, not
        // a YieldStatementSyntax. Mutation: condition 4's pattern match.
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                    {
                        foreach (var inner in items)
                        {
                            yield return Span[inner];
                        }
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheForEachDeconstructs_ReportsBcf1002()
    {
        // A deconstructing `foreach (var (a, b) in ...)` parses as ForEachVariableStatementSyntax, a
        // sibling of ForEachStatementSyntax under CommonForEachStatementSyntax rather than a subtype of
        // it. Mutation: condition 1's pattern match.
        AssertReportsBcf1002(
            """
            Part(IEnumerable<(string A, string B)> items)
                {
                    foreach (var (a, b) in items)
                    {
                        yield return Span[a];
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheLoopBodyIsNotABlock_ReportsBcf1002()
    {
        // A braceless loop body (`foreach (...) yield return x;`) is refused. Mutation: condition 3
        // (`last.Statement is not BlockSyntax`).
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                        yield return Span[item];
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenALeadingStatementDeclaresTheBuildersName_ReportsBcf1002()
    {
        // Mutation: condition 7's DeclaresReservedName(block) scan.
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    var __builder = items;
                    foreach (var item in __builder)
                    {
                        yield return Span[item];
                    }
                }
            """);
    }

    [Fact]
    public void IteratorViewPart_WhenTheYieldedExpressionDeclaresAGeneratorReservedName_ReportsBcf1002()
    {
        // The reserved-name scan covers the yielded expression too, since it sits inside the outer block
        // DeclaresReservedName walks. The pattern designator sits inside Span[...]'s single argument (the
        // same shape ViewPartTransplantTests' "declares inside its expression" cases use), not as the
        // yielded expression's own top-level branch, so the one thing that changes under mutation is
        // whether the reserved name is caught -- a ternary directly between two Views is a separate,
        // already-untranslatable shape that would reject regardless. Mutation: condition 7's
        // DeclaresReservedName(block) scan.
        AssertReportsBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var item in items)
                    {
                        yield return Span[item is { Length: > 0 } __bcf_ok ? __bcf_ok : "n"];
                    }
                }
            """);
    }

    /// <summary>
    /// The one deliberate, already-adjudicated gap in <see cref="RenderExpressionAnalyzer.TryReadIteratorForEach"/>'s
    /// reserved-name scan: the `foreach`'s own iteration-variable token is a bare <c>SyntaxToken</c>
    /// directly on <c>ForEachStatementSyntax</c>, with no declarator or designation node for
    /// <c>TryGetDeclaredLocalIdentifier</c> to match, so the scan does not see it. This is not a bug --
    /// the iteration variable is minted (<c>__bcf_item_N</c>), not transplanted, exactly like a
    /// <c>ForEach</c> content lambda's own parameter, so the author's original token never survives into
    /// generated code for it to collide with anything. Only declaration acceptance is confirmed here;
    /// confirming the expansion itself compiles needs a call site (a later task).
    /// </summary>
    [Fact]
    public void IteratorViewPart_WhenTheIterationVariableCarriesTheBuildersName_MintsOverIt()
    {
        AssertDoesNotReportBcf1002(
            """
            Part(IEnumerable<string> items)
                {
                    foreach (var __builder in items)
                    {
                        yield return Span[__builder];
                    }
                }
            """);
    }
}

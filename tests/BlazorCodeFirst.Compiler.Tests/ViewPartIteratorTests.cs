using System.Globalization;
using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The <c>[ViewPart]</c> iterator shape (ARCHITECTURE.md §5.3, extended to the one shape `if`/`switch`
/// cannot cover -- <see cref="RenderExpressionAnalyzer.TryReadIteratorForEach"/>'s remarks): declaration
/// acceptance and rejection through <c>ViewPartDefinitionFactory</c> (the tests using <see cref="Run(string)"/>
/// below, where a declaration stands alone with nothing calling it), and, once a call site can reach it
/// through the spread syntax (<see cref="RenderExpressionAnalyzer.AnalyzeSplice"/>, #316), the expansion
/// that call site produces (the tests using <see cref="RunCall"/>).
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

    /// <summary>
    /// A component whose members are <c>$MEMBERS$</c> (every <c>[ViewPart]</c> declaration a call-site test
    /// needs, plus any supporting fields/records) and whose <c>Body</c> is <c>$BODY$</c>. Unlike
    /// <see cref="Host"/>, this template fixes neither a part's signature nor its return type, since a
    /// call-site test may need more than one part, an ordinary (non-iterator) part beside an iterator one, or
    /// a part whose declaration is itself deliberately wrong.
    /// </summary>
    private const string CallHost = """
        using System.Collections.Generic;
        using System.Linq;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            $MEMBERS$

            protected override View Body => $BODY$;
        }
        """;

    /// <summary>The <c>Item</c> record and an <c>_items</c> field of iterator parts below read.</summary>
    private const string ItemMembers = """
        private sealed record Item(int Id, string Name);
        private readonly IReadOnlyList<Item> _items = new List<Item> { new Item(1, "a"), new Item(2, "b") };
        """;

    /// <summary>
    /// The worked example (task brief): a keyed iterator part over <see cref="ItemMembers"/>'s
    /// <c>_items</c>.
    /// </summary>
    private const string RowsPart = """
        [ViewPart]
        private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
            {
                foreach (var item in items)
                {
                    yield return Li.Key(item.Id)[item.Name];
                }
            }
        """;

    private static GeneratorRunResult RunCall(string body, string members) =>
        CompilationTestHost.RunGenerator(
            CallHost.Replace("$MEMBERS$", members).Replace("$BODY$", body));

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
    /// generated code for it to collide with anything. Declaration acceptance is confirmed here;
    /// <see cref="IteratorViewPart_WhenTheIterationVariableCarriesTheBuildersName_ExpandsAndCompilesAtACallSite"/>
    /// confirms the expansion itself compiles, now that a call site can reach it (#316).
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

    /// <summary>The call-site half of <see cref="IteratorViewPart_WhenTheIterationVariableCarriesTheBuildersName_MintsOverIt"/>.</summary>
    [Fact]
    public void IteratorViewPart_WhenTheIterationVariableCarriesTheBuildersName_ExpandsAndCompilesAtACallSite()
    {
        const string members = """
            [ViewPart]
            private static IEnumerable<View> Rows(IEnumerable<string> items)
                {
                    foreach (var __builder in items)
                    {
                        yield return Span[__builder];
                    }
                }
            """;

        var result = RunCall("""Div[[.. Rows(new[] { "a", "b" })]]""", members);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    // ---------------------------------------------------------------------------
    // Call-site acceptance: `.. Rows(_items)` reaches the declaration above through
    // RenderExpressionAnalyzer.AnalyzeSplice's new branch (#316).
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The task brief's worked example: <c>Ul[[.. Rows(_items)]]</c> emits one region and one static
    /// content range reused by every iteration, structurally matching <c>ForEach</c>'s own emission.
    /// </summary>
    [Fact]
    public void IteratorViewPart_WhenSplicedIntoAnElement_EmitsOneRegionAndOneStaticContentRange()
    {
        var result = RunCall("""Ul[[.. Rows(_items)]]""", ItemMembers + RowsPart);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");

        // The argument local, the region, the loop, and the keyed/content frames the loop body opens --
        // the brief's worked example, reproduced.
        Assert.Contains("__bcf_arg_1_0 = _items;", generated);
        Assert.Contains("__builder.OpenRegion(1);", generated);
        Assert.Contains("foreach (var __bcf_item_2 in __bcf_arg_1_0)", generated);
        Assert.Contains("__builder.OpenElement(2, \"li\");", generated);
        Assert.Contains("__builder.SetKey(__bcf_item_2.Id);", generated);
        Assert.Contains("__builder.AddContent(3, __bcf_item_2.Name);", generated);
        Assert.Contains("__builder.CloseRegion();", generated);

        CompilationTestHost.AssertOutputCompiles(result);
        CompilationTestHost.AssertGeneratedOutputHasNoWarnings(result);
    }

    [Fact]
    public void IteratorViewPart_WhenSplicedIntoAnElement_ReusesTheContentRangeEveryIteration()
    {
        var result = RunCall("""Ul[[.. Rows(_items)]]""", ItemMembers + RowsPart);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The loop, not repeated emission, is what applies the content range on every iteration: the
        // region open and the frame it wraps are each written once in the generated source, outside (in
        // source-text terms, textually preceding/following) any per-iteration duplication.
        Assert.Single(Regex.Matches(generated, "__builder\\.OpenRegion\\("));
        Assert.Single(Regex.Matches(generated, "OpenElement\\(2, \"li\"\\)"));
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IteratorViewPart_WhenTheYieldedElementCarriesKey_EmitsSetKeyOnItsOwnFrame()
    {
        var result = RunCall("""Ul[[.. Rows(_items)]]""", ItemMembers + RowsPart);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // SetKey is emitted immediately after the frame it keys, never after OpenRegion.
        Assert.Matches(
            new Regex(@"OpenElement\(2, ""li""\);\s*__builder\.SetKey\(__bcf_item_2\.Id\);"),
            generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IteratorViewPart_WhenNoKeyIsWritten_EmitsNoSetKey()
    {
        const string members = """
            private sealed record Item(int Id, string Name);
            private readonly IReadOnlyList<Item> _items = new List<Item> { new Item(1, "a") };

            [ViewPart]
            private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
                {
                    foreach (var item in items)
                    {
                        yield return Li[item.Name];
                    }
                }
            """;

        var result = RunCall("""Ul[[.. Rows(_items)]]""", members);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("SetKey", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IteratorViewPart_WhenTheBodyHasLeadingStatements_TransplantsThemAheadOfTheRegion()
    {
        const string members = """
            private sealed record Item(int Id, string Name);
            private readonly IReadOnlyList<Item> _items = new List<Item> { new Item(1, "a") };

            [ViewPart]
            private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
                {
                    var prefix = "row-";
                    foreach (var item in items)
                    {
                        yield return Li[prefix + item.Name];
                    }
                }
            """;

        var result = RunCall("""Ul[[.. Rows(_items)]]""", members);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");
        Assert.Contains("__bcf_local_", generated);

        var localIndex = generated.IndexOf("__bcf_local_", StringComparison.Ordinal);
        var regionIndex = generated.IndexOf("__builder.OpenRegion(", StringComparison.Ordinal);
        Assert.True(
            localIndex >= 0 && regionIndex >= 0 && localIndex < regionIndex,
            $"Expected the leading local ahead of the region. local={localIndex}, region={regionIndex}. {generated}");

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IteratorViewPart_WhenTheLoopBodyHasLeadingStatements_TransplantsThemInsideTheLoop()
    {
        const string members = """
            private sealed record Item(int Id, string Name);
            private readonly IReadOnlyList<Item> _items = new List<Item> { new Item(1, "a") };

            [ViewPart]
            private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
                {
                    foreach (var item in items)
                    {
                        var label = item.Name.ToUpperInvariant();
                        yield return Li[label];
                    }
                }
            """;

        var result = RunCall("""Ul[[.. Rows(_items)]]""", members);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");

        var foreachIndex = generated.IndexOf("foreach (var __bcf_item_", StringComparison.Ordinal);
        var localIndex = generated.IndexOf("__bcf_local_", StringComparison.Ordinal);
        var closeRegionIndex = generated.IndexOf("__builder.CloseRegion();", StringComparison.Ordinal);
        Assert.True(
            foreachIndex >= 0 && localIndex >= 0 && closeRegionIndex >= 0
                && foreachIndex < localIndex && localIndex < closeRegionIndex,
            $"Expected the leading local inside the loop. foreach={foreachIndex}, local={localIndex}, "
                + $"closeRegion={closeRegionIndex}. {generated}");

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IteratorViewPart_WhenTakingParameters_DeclaresOneTypedLocalPerArgument()
    {
        const string members = """
            private sealed record Item(int Id, string Name);
            private readonly IReadOnlyList<Item> _items = new List<Item> { new Item(1, "a") };

            [ViewPart]
            private static IEnumerable<View> Rows(IReadOnlyList<Item> items, string prefix)
                {
                    foreach (var item in items)
                    {
                        yield return Li[prefix + item.Name];
                    }
                }
            """;

        var result = RunCall("""Ul[[.. Rows(_items, "row-")]]""", members);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");
        Assert.Contains("__bcf_arg_1_0 = _items;", generated);
        Assert.Contains("string __bcf_arg_1_1 = \"row-\";", generated);

        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// The load-bearing test for the ordinal/minted-name correspondence: two independent expansions of
    /// the same iterator part must mint two DISTINCT iteration-variable names, and each loop's own
    /// content must read only its own loop's variable -- never the sibling expansion's.
    /// </summary>
    [Fact]
    public void IteratorViewPart_WhenCalledTwice_NamesEachExpansionsIterationVariableApart()
    {
        var result = RunCall(
            """Div[Ul[[.. Rows(_items)]], Ul[[.. Rows(_items)]]]""",
            ItemMembers + RowsPart);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);

        var loopVariables = Regex.Matches(generated, @"foreach \(var (__bcf_item_\d+) in")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.Equal(2, loopVariables.Count);
        Assert.Equal(2, loopVariables.Distinct().Count());

        // Each expansion's own loop variable is what its own SetKey/AddContent read -- not the other
        // expansion's. A collision here (both loops reading the same name, or one loop reading the
        // other's) would still leave "2 distinct names" true above while silently emitting wrong code.
        foreach (var name in loopVariables)
        {
            Assert.Contains($"__builder.SetKey({name}.Id);", generated);
            Assert.Matches(new Regex($@"__builder\.AddContent\(\d+, {name}\.Name\);"), generated);
        }
    }

    [Fact]
    public void IteratorViewPart_WhenTheLoopSourceDeclaresALocal_AcceptsItInTheYieldedExpression()
    {
        const string members = """
            private sealed record Item(int Id, string Name);
            private readonly IReadOnlyList<Item> _items = new List<Item> { new Item(1, "a") };

            private static IReadOnlyList<Item> Take(IReadOnlyList<Item> items, out int count)
                {
                    count = items.Count;
                    return items;
                }

            [ViewPart]
            private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
                {
                    foreach (var item in Take(items, out var count))
                    {
                        yield return Li[item.Name + count.ToString()];
                    }
                }
            """;

        var result = RunCall("""Ul[[.. Rows(_items)]]""", members);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A nested (ordinary) <c>ForEach</c> content lambda inside the yielded expression, ending in a
    /// native `if`, degrades -- its own BCF2002, from its own Transplantable position -- but the
    /// enclosing iterator loop itself has no alternative spelling to prefer and reports nothing.
    /// </summary>
    [Fact]
    public void IteratorViewPart_WhenYieldingANestedIfOrSwitch_ReportsBcf2002ForThatConstructOnly()
    {
        const string members = """
            private sealed record Item(int Id, string Name);
            private readonly IReadOnlyList<Item> _items = new List<Item> { new Item(1, "a") };

            private static IEnumerable<string> Sub(Item item)
                {
                    yield return item.Name;
                }

            [ViewPart]
            private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
                {
                    foreach (var item in items)
                    {
                        yield return Div[
                            ForEach(Sub(item), null, x =>
                                {
                                    if (x.Length > 0)
                                    {
                                        return Span[x];
                                    }
                                    else
                                    {
                                        return Span["-"];
                                    }
                                })
                        ];
                    }
                }
            """;

        var result = RunCall("""Ul[[.. Rows(_items)]]""", members);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");
        Assert.Single(result.Diagnostics, d => d.Id == "BCF2002");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>Global Constraint 2, pinned directly: a spliced iterator part reports no BCF2002 at all.</summary>
    [Fact]
    public void IteratorViewPart_WhenSpliced_ReportsNoBcf2002()
    {
        var result = RunCall("""Ul[[.. Rows(_items)]]""", ItemMembers + RowsPart);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF2002");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IteratorViewPart_WhenSplicedInsideAForEachContent_NestsTwoRegions()
    {
        const string members = """
            private sealed record Item(int Id, string Name);
            private sealed record Group(int Id, List<Item> Items);
            private readonly List<Group> _groups = new()
                {
                    new Group(1, new List<Item> { new Item(1, "a") }),
                };

            [ViewPart]
            private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
                {
                    foreach (var item in items)
                    {
                        yield return Li[item.Name];
                    }
                }
            """;

        var result = RunCall(
            """ForEach(_groups, null, g => Div[[.. Rows(g.Items)]])""",
            members);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");
        Assert.Equal(2, Regex.Count(generated, "__builder\\.OpenRegion\\("));
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IteratorViewPart_WhenSplicedInsideAnotherViewPart_ExpandsBothInlined()
    {
        const string members = """
            private sealed record Item(int Id, string Name);
            private readonly IReadOnlyList<Item> _items = new List<Item> { new Item(1, "a") };

            [ViewPart]
            private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
                {
                    foreach (var item in items)
                    {
                        yield return Li[item.Name];
                    }
                }

            [ViewPart]
            private static View Widget(IReadOnlyList<Item> items) => Ul[[.. Rows(items)]];
            """;

        var result = RunCall("""Widget(_items)""", members);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1002" or "BCF1003");
        Assert.Contains("__builder.OpenRegion(", generated);
        Assert.Contains("foreach (var __bcf_item_", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    // ---------------------------------------------------------------------------
    // Call-site rejection: Global Constraint 5 -- the widening is for iterator [ViewPart] calls only.
    // ---------------------------------------------------------------------------

    [Fact]
    public void IteratorViewPart_WhenSpreadOfANonAttributedSequenceMethod_ReportsBcf1003()
    {
        const string members = """
            private static IEnumerable<View> GetViews() => new View[] { Span["x"] };
            """;

        var result = RunCall("""Ul[[.. GetViews()]]""", members);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void IteratorViewPart_WhenSpreadOfAStoredViewArray_ReportsBcf1003()
    {
        const string members = """
            private readonly View[] _views = [];
            """;

        var result = RunCall("""Ul[[.. _views]]""", members);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void IteratorViewPart_WhenSpreadReturnsViewArrayRatherThanIEnumerable_ReportsBcf1003()
    {
        const string members = """
            [ViewPart]
            private static View[] Rows() => new View[] { Span["x"] };
            """;

        var result = RunCall("""Ul[[.. Rows()]]""", members);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }
}

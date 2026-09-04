namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// A loop source -- <c>ForEach</c>'s source argument, or a native <c>foreach</c>'s source inside a
/// <c>[ViewPart]</c> iterator's own body -- that calls a <c>[ViewPart]</c> renders nothing at runtime:
/// the callee's body is built from the design-time surface, so every yielded item is empty (#578).
/// BCF3043 refuses the shape at the loop's source position, the one position <c>ClassifyCallee</c> was
/// never asked about before this fix.
/// </summary>
public sealed class LoopSourceViewPartTests
{
    private const string ItemMembers = """
        private sealed record Item(int Id, string Name);
        private readonly IReadOnlyList<Item> _items =
            new List<Item> { new Item(1, "a"), new Item(2, "b") };
        """;

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

    /// <summary>A component whose members are <c>$MEMBERS$</c> and whose <c>Body</c> is <c>$BODY$</c>.</summary>
    private const string CallHost = """
        using System.Collections.Generic;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            $MEMBERS$

            protected override View Body => $BODY$;
        }
        """;

    /// <summary>A component whose members are <c>$MEMBERS$</c>, standing alone -- nothing calls them.</summary>
    private const string DeclarationHost = """
        using System.Collections.Generic;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Span["Body"];

            $MEMBERS$
        }
        """;

    private static GeneratorRunResult RunCall(string body, string members) =>
        CompilationTestHost.RunGenerator(
            CallHost.Replace("$MEMBERS$", members).Replace("$BODY$", body));

    private static GeneratorRunResult RunDeclaration(string members) =>
        CompilationTestHost.RunGenerator(DeclarationHost.Replace("$MEMBERS$", members));

    [Fact]
    public void ForEachCombinator_WhenSourceCallsAViewPart_ReportsBcf3043()
    {
        var result = RunCall(
            "ForEach(Rows(_items), item => 0, item => Span[\"x\"])",
            RowsPart + "\n\n" + ItemMembers);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3043");
    }

    [Fact]
    public void ForEachCombinator_WhenSourceIsAPlainCollection_DoesNotReportBcf3043()
    {
        var result = RunCall(
            "ForEach(_items, item => item.Id, item => Span[item.Name])",
            ItemMembers);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3043");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachCombinator_WhenSourceCallsAnOrdinaryMethod_DoesNotReportBcf3043()
    {
        var result = RunCall(
            "ForEach(GetItems(), item => item.Id, item => Span[item.Name])",
            ItemMembers + "\n\n" + "private IReadOnlyList<Item> GetItems() => _items;");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3043");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void NativeForEach_InsideAnIteratorViewPart_WhenSourceCallsAnotherViewPart_ReportsBcf3043()
    {
        const string outerUsesRowsAsSource = """
            [ViewPart]
            private static IEnumerable<View> Outer(IReadOnlyList<Item> items)
            {
                foreach (var v in Rows(items))
                {
                    yield return Span["x"];
                }
            }
            """;

        var result = RunDeclaration(RowsPart + "\n\n" + outerUsesRowsAsSource + "\n\n" + ItemMembers);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3043");
    }

    [Fact]
    public void NativeForEach_InsideAnIteratorViewPart_WhenSourceIsAPlainCollection_DoesNotReportBcf3043()
    {
        var result = RunDeclaration(RowsPart + "\n\n" + ItemMembers);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3043");
    }

    [Fact]
    public void SpreadOfIteratorViewPart_InContentPosition_DoesNotReportBcf3043()
    {
        var result = RunCall("Ul[[.. Rows(_items)]]", RowsPart + "\n\n" + ItemMembers);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3043");
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

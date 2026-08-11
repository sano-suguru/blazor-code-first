using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// What the content-slot surface refuses, and which layer refuses it (#34, #176).
/// </summary>
/// <remarks>
/// The division is the point of the design. <c>SlotView</c> declares no conversion to <c>View</c>, so
/// almost every mistake is a C# error before the generator runs — a forgotten bracket, a decoration, the
/// positional spelling. The one thing the type system cannot see is a <c>Slot</c> in a declaration that
/// receives no caller content, and that is the one new diagnostic (BCF3025). The two halves are tested
/// separately below so a regression that turns a compile error into a diagnostic, or the reverse, is visible.
/// </remarks>
public sealed class ComposableContentDiagnosticTests
{
    [Theory]
    // A component's own design-time expression receives no brackets, so it has no slot to fill.
    [InlineData(
        """
        [Composable] private static View Ok() => Span["x"];
        protected override View Body => Div[Slot];
        """,
        "is written where no caller content is received")]
    // A part returning View is called without brackets, so likewise.
    [InlineData(
        """
        [Composable] private static View Bad() => Div[Slot];
        protected override View Body => Bad();
        """,
        "is written where no caller content is received")]
    // Declared to take content and never places it: the caller is required to supply content that would
    // then be discarded.
    [InlineData(
        """
        [Composable] private static SlotView None() => Div.Class("x")["nothing"];
        protected override View Body => None()["c"];
        """,
        "is never named in 'None'")]
    // Two slots would emit the caller's content twice from one bracket.
    [InlineData(
        """
        [Composable] private static SlotView Two() => Div[Slot, Slot];
        protected override View Body => Two()["c"];
        """,
        "is named 2 times in 'Two'")]
    public void Slot_WhereNoCallerContentIsBound_ReportsBCF3025(string members, string message)
    {
        var result = RunComponent(members);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3025");

        Assert.Contains(message, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    // The bracket is mandatory because SlotView is not a View, so a call without it is not a child.
    [InlineData("""
        [Composable] private static SlotView Card() => Div[Slot];
        protected override View Body => Div[Card()];
        """)]
    // Nor a design-time expression on its own.
    [InlineData("""
        [Composable] private static SlotView Card() => Div[Slot];
        protected override View Body => Card();
        """)]
    // The positional spelling #176 decided against does not exist: there is no parameter to bind to.
    [InlineData("""
        [Composable] private static SlotView Card() => Div[Slot];
        protected override View Body => Card(P["x"])["c"];
        """)]
    // Decorations are extension methods on ElementView, so a SlotView finds none -- the same
    // mechanism that makes Div["x"].Class("y") unwritable rather than a second supported style.
    [InlineData("""
        [Composable] private static SlotView Card() => Div[Slot];
        protected override View Body => Card().Class("x")["c"];
        """)]
    public void ContentSurface_MisuseThatCSharpAlreadyRefuses_NeedsNoDiagnostic(string members)
    {
        var result = RunComponent(members);

        var errors = result.OutputCompilation
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error
                && d.Id.StartsWith("CS", System.StringComparison.Ordinal))
            .ToImmutableArray();

        Assert.False(
            errors.IsEmpty,
            "Expected C# to reject this shape on its own, but the compilation reported no CS error.");

        // No BCF3025 either: the slot itself is written correctly in every row, and inventing a second
        // report for a shape the language already rejects is what this design avoids.
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3025");
    }

    /// <summary>
    /// A <c>View</c> parameter is a content slot, so it requires the return type that says the part takes
    /// content. Admitting it on a <c>View</c>-returning part would readmit <c>Card("t", P["x"])</c> as a
    /// second spelling of what brackets write, which is the one thing #176 rules out.
    /// </summary>
    [Fact]
    public void ViewParameter_OnAViewReturningComposable_ReportsBCF1002()
    {
        var result = RunComponent("""
            [Composable] private static View Card(View content) => Div[content];
            protected override View Body => Card(P["x"]);
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            "View parameters are content slots and require a SlotView return type",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// An omitted optional would have to mean "no content", and <c>default(View)</c> is not that: it is the
    /// inert marker, which expansion has no subtree for.
    /// </summary>
    [Fact]
    public void OptionalViewParameter_ReportsBCF1002()
    {
        var result = RunComponent("""
            [Composable] private static SlotView Card(View header = default) => Div[header, Slot];
            protected override View Body => Card()["c"];
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            "View parameter 'header' must not be optional",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// ElementView stays rejected. A childless element is an ElementView rather than a View, so
    /// admitting it would give content a second parameter type and, with it, a second spelling; a caller
    /// passes a childless element as content by writing <c>Div[…]</c> or <c>Fragment(Div)</c>.
    /// </summary>
    [Fact]
    public void ElementViewParameter_OnAContentTakingComposable_ReportsBCF1002()
    {
        var result = RunComponent("""
            [Composable] private static SlotView Card(ElementView head) => Div[head, Slot];
            protected override View Body => Card(Div)["c"];
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            "ElementView parameters are unsupported",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Content has no value, so a reference to it in a value position is reported as the unsupported reference
    /// it is. Before this was checked, such a reference minted an expression hole at a content ordinal and the
    /// substitution threw, which the generator driver turned into CS8785 — no generated source, no location,
    /// and nothing naming what the author had written.
    /// </summary>
    [Theory]
    // A ForEach key is a value expression.
    [InlineData(
        """
        private List<string> _xs = new();
        [Composable] private static SlotView Rows(List<string> xs) => Ul[ForEach(xs, x => Slot, x => Li["a"])];
        protected override View Body => Rows(_xs)[Li["c"]];
        """,
        "Slot")]
    // So is an attribute value, here reached through a method call on a View parameter.
    [InlineData(
        """
        private static string Describe(View v) => "x";
        [Composable] private static SlotView Card(View header) => Div.Attr("data-x", Describe(header))[Slot];
        protected override View Body => Card(Span["h"])[P["c"]];
        """,
        "header")]
    public void CallerContentInAValuePosition_ReportsBCF1002(string members, string name)
    {
        var result = RunComponent(members);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            $"'{name}' is caller-supplied content, which has no value",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));

        // The point of reporting it here: the generator must not fall over.
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "CS8785");
    }

    /// <summary>
    /// A slot as a ForEach content root is keyable when the caller supplied a keyable element there, and the
    /// resolver has the call's content in hand at that point. Reporting BCF3003 anyway gave the author a
    /// diagnostic they could only fix inside someone else's <c>[Composable]</c>.
    /// </summary>
    [Fact]
    public void SlotRootedForEachContent_WhenTheCallerSuppliesAKeyableElement_IsNotReported()
    {
        var result = RunComponent("""
            private List<string> _xs = new();
            [Composable] private static SlotView Bare() => Slot;
            protected override View Body => Ul[ForEach(_xs, x => x, x => Bare()[Li[x]])];
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// The other side of it: with no keyable element to find, the report still happens. Here the caller
    /// supplies a bare <c>If</c>, which opens no frame of its own.
    /// </summary>
    [Fact]
    public void SlotRootedForEachContent_WhenTheCallerSuppliesARegion_IsStillReported()
    {
        var result = RunComponent("""
            private List<string> _xs = new();
            private bool _flag;
            [Composable] private static SlotView Bare() => Slot;
            protected override View Body =>
                Ul[ForEach(_xs, x => x, x => Bare()[If(_flag, () => Li[x])])];
            """);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3003");
    }

    /// <summary>
    /// <c>SlotView</c> is an inert design-time marker, so it belongs to BCF3014 beside <c>View</c>,
    /// <c>ElementView</c> and <c>ComponentView&lt;T&gt;</c>: a call before its brackets boxes to
    /// <c>object</c> and would otherwise be emitted verbatim into a component parameter.
    /// </summary>
    [Fact]
    public void SlotViewInAScalarParamValue_ReportsBCF3014()
    {
        var source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public class Target : ComponentBase
            {
                [Parameter] public object? Payload { get; set; }
            }

            public partial class C : BodyComponentBase
            {
                [Composable] private static SlotView Card() => Div[Slot];

                protected override View Body =>
                    Component<Target>().Param(t => t.Payload, Card());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3014");
    }

    private static GeneratorRunResult RunComponent(string members)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                {{members}}
            }
            """;

        return CompilationTestHost.RunGenerator(source);
    }
}

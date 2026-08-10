using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// What the content-slot surface refuses, and which layer refuses it (#34, #176).
/// </summary>
/// <remarks>
/// The division is the point of the design. <c>ContentView</c> declares no conversion to <c>View</c>, so
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
        [Composable] private static ContentView None() => Div.Class("x")["nothing"];
        protected override View Body => None()["c"];
        """,
        "is never named in 'None'")]
    // Two slots would emit the caller's content twice from one bracket.
    [InlineData(
        """
        [Composable] private static ContentView Two() => Div[Slot, Slot];
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
    // The bracket is mandatory because ContentView is not a View, so a call without it is not a child.
    [InlineData("""
        [Composable] private static ContentView Card() => Div[Slot];
        protected override View Body => Div[Card()];
        """)]
    // Nor a design-time expression on its own.
    [InlineData("""
        [Composable] private static ContentView Card() => Div[Slot];
        protected override View Body => Card();
        """)]
    // The positional spelling #176 decided against does not exist: there is no parameter to bind to.
    [InlineData("""
        [Composable] private static ContentView Card() => Div[Slot];
        protected override View Body => Card(P["x"])["c"];
        """)]
    // Decorations are extension methods on ElementBuilder, so a ContentView finds none -- the same
    // mechanism that makes Div["x"].Class("y") unwritable rather than a second supported style.
    [InlineData("""
        [Composable] private static ContentView Card() => Div[Slot];
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
            "View parameters are content slots and require a ContentView return type",
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
            [Composable] private static ContentView Card(View header = default) => Div[header, Slot];
            protected override View Body => Card()["c"];
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            "View parameter 'header' must not be optional",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// ElementBuilder stays rejected. A childless element is an ElementBuilder rather than a View, so
    /// admitting it would give content a second parameter type and, with it, a second spelling; a caller
    /// passes a childless element as content by writing <c>Div[…]</c> or <c>Fragment(Div)</c>.
    /// </summary>
    [Fact]
    public void ElementBuilderParameter_OnAContentTakingComposable_ReportsBCF1002()
    {
        var result = RunComponent("""
            [Composable] private static ContentView Card(ElementBuilder head) => Div[head, Slot];
            protected override View Body => Card(Div)["c"];
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            "ElementBuilder parameters are unsupported",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    private static GeneratorRunResult RunComponent(string members)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                {{members}}
            }
            """;

        return CompilationTestHost.RunGenerator(source);
    }
}

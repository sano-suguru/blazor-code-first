using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// A local declared in the header of a lowered construct and read from the body it encloses: the
/// <c>If</c> condition, and the source of a <c>ForEach</c> or of the <c>Select</c> a spliced child list
/// is sugar for (#361).
/// </summary>
/// <remarks>
/// The admission is exactly as wide as the generated nesting proves safe, and no wider. An <c>if</c>
/// header scopes over both branches and a <c>foreach</c> header over the loop body, so a declaration
/// there reaches every reference the author's own file kept together. A component slot is lowered into a
/// <c>RenderFragment</c> lambda of its own, so a declaration in one slot does <em>not</em> reach a
/// sibling slot or a parameter; those shapes stay BCF1002 and are asserted here alongside the ones that
/// now pass, because a rule this narrow is only meaningful with its boundary pinned.
/// </remarks>
public sealed class LoweredHeaderLocalTests
{
    private const string Host = """
        using System.Collections.Generic;
        using System.Linq;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private static int Take(out int v) { v = 1; return 0; }
            private static IEnumerable<int> Items(int seed) => new[] { seed };
            protected override View Body => $BODY$;
        }
        """;

    private const string CardSource = """
        using Microsoft.AspNetCore.Components;
        public class Card : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter] public RenderFragment? Footer { get; set; }
        }
        """;

    private static GeneratorRunResult Run(string body) =>
        CompilationTestHost.RunGenerator(
            ("Host.cs", Host.Replace("$BODY$", body)),
            ("Card.cs", CardSource));

    private static string AssertAccepted(string body)
    {
        var result = Run(body);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BCF1002" or "BCF1003");
        CompilationTestHost.AssertOutputCompiles(result);
        CompilationTestHost.AssertGeneratedOutputHasNoWarnings(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    private static void AssertRefused(string body)
    {
        var result = Run(body);

        Assert.Contains(
            result.Diagnostics,
            static d => d.Id == "BCF1002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void IfCondition_DeclaringALocalReadFromTheThenBranch_IsAccepted()
    {
        var generated = AssertAccepted(
            """If(Take(out var n) == 0, () => Span[n.ToString()], () => Span["x"])""");

        // The declaration lands in the generated `if` header and the reference in the branch it scopes
        // over, which is the whole claim. `var` is written as its inferred type for the reason every
        // other transplanted declaration is (#342).
        Assert.Contains(
            "if (global::C.Take(out int n) == 0)", generated, System.StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(2, n.ToString());", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void IfCondition_DeclaringALocalReadFromTheOtherwiseBranch_IsAccepted() =>
        AssertAccepted("""If(Take(out var n) == 0, () => Span["x"], () => Span[n.ToString()])""");

    [Fact]
    public void ForEachSource_DeclaringALocalReadFromTheContent_IsAccepted()
    {
        var generated = AssertAccepted(
            """ForEach(Items(Take(out var n)), i => i, i => Span[n.ToString()])""");

        Assert.Contains(
            "in global::C.Items(global::C.Take(out int n))", generated, System.StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(2, n.ToString());", generated, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The key is emitted as a <c>SetKey</c> inside the loop body, so the source's header scope reaches
    /// it for the same reason the content does.
    /// </summary>
    [Fact]
    public void ForEachSource_DeclaringALocalReadFromTheKey_IsAccepted() =>
        AssertAccepted("""ForEach(Items(Take(out var n)), i => i + n, i => Span[i.ToString()])""");

    [Fact]
    public void SpliceSource_DeclaringALocalReadFromTheProjection_IsAccepted() =>
        AssertAccepted(
            """Div[[..Items(Take(out var n)).Select(i => Span[n.ToString()])]]""");

    /// <summary>A nested body still reads a local from the header two constructs out.</summary>
    [Fact]
    public void IfCondition_DeclaringALocalReadFromANestedForEach_IsAccepted() =>
        AssertAccepted(
            """
            If(Take(out var n) == 0,
                () => ForEach(Items(0), i => i, i => Span[n.ToString()]),
                () => Span["x"])
            """);

    [Fact]
    public void ComponentSlot_DeclaringALocalReadFromASiblingSlot_StaysRefused() =>
        AssertRefused(
            """
            Component<Card>()
                .Param(c => c.Footer, Div[Take(out var n).ToString()])
                .Param(c => c.ChildContent, Div[n.ToString()])
            """);

    [Fact]
    public void ComponentSlot_DeclaringALocalReadFromASiblingParameter_StaysRefused() =>
        AssertRefused(
            """
            Component<Card>()
                .Param(c => c.Footer, Div[Take(out var n).ToString()])
                .Param(c => c.Title, n.ToString())
            """);

    /// <summary>
    /// A sibling argument of the same call is not a header: nothing in the generated code puts the
    /// declaration in a scope enclosing the reference, so this keeps the refusal it has always had.
    /// </summary>
    [Fact]
    public void SiblingAttribute_DeclaringALocalReadFromALaterAttribute_StaysRefused() =>
        AssertRefused("""Div.Attr("a", Take(out var n).ToString()).Attr("b", n.ToString())""");

    /// <summary>
    /// Both positions that normalize a body read the one check, so closing it closes both (#361). The
    /// component's own design-time expression is covered by every case above; this is the
    /// <c>[ViewPart]</c> half.
    /// </summary>
    [Fact]
    public void ViewPartBody_WithALocalDeclaredInAnIfCondition_IsAccepted()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                private static int Take(out int v) { v = 1; return 0; }

                [ViewPart]
                private static View Row(string label) =>
                    If(Take(out var n) == 0, () => Span[label + n.ToString()], () => Span["x"]);

                protected override View Body => Row("a");
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BCF1002" or "BCF1003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// BCF1002 names the position it is reported at. 付録A describes it as the view part diagnostic, and
    /// it is also the report for a component's own design-time expression, so calling that expression a
    /// "ViewPart method" named the wrong thing (#361).
    /// </summary>
    [Fact]
    public void DesignTimeExpression_RefusedForAnUnsupportedReference_IsNamedAsAnExpressionNotAMethod()
    {
        var result = Run("""Div.Attr("a", Take(out var n).ToString()).Attr("b", n.ToString())""");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            "The Body design-time expression of 'C' is unsupported:",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void ViewPartMethod_RefusedForAnUnsupportedReference_KeepsItsOwnWording()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                private static int Take(out int v) { v = 1; return 0; }

                [ViewPart]
                private static View Row(string label) =>
                    Div.Attr("a", Take(out var n).ToString()).Attr("b", n.ToString())[label];

                protected override View Body => Row("a");
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(
            "ViewPart method 'Row' is unsupported:",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            System.StringComparison.Ordinal);
    }
}

using System;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// A local declared in the header of a lowered construct and read from the body it encloses: the
/// <c>If</c> condition, and the source of a <c>ForEach</c> or of the <c>Select</c> a spliced child list
/// is sugar for (#361). ARCHITECTURE.md §2.3 states the rule and why nothing wider is admitted; the
/// refused shapes are here beside the accepted ones because a rule this narrow is only meaningful with
/// its boundary pinned.
/// </summary>
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
            $PART$
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

    /// <summary>The one body both wording tests and the sibling-attribute refusal run.</summary>
    private const string SiblingAttributeBody =
        """Div.Attr("a", Take(out var n).ToString()).Attr("b", n.ToString())""";

    private static GeneratorRunResult Run(string body, string part = "") =>
        CompilationTestHost.RunGenerator(
            ("Host.cs", Host.Replace("$BODY$", body).Replace("$PART$", part)),
            ("Card.cs", CardSource));

    /// <summary>As <see cref="Run"/>, for a <c>[ViewPart]</c> whose body is <paramref name="partBody"/>.</summary>
    private static GeneratorRunResult RunViewPart(string partBody) =>
        Run(
            """Row("a")""",
            $$"""
            [ViewPart]
                private static View Row(string label) => {{partBody}};
            """);

    private static string AssertAccepted(string body)
    {
        var result = Run(body);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BCF1002" or "BCF1003");
        CompilationTestHost.AssertOutputCompiles(result);
        CompilationTestHost.AssertGeneratedOutputHasNoWarnings(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    /// <summary>
    /// Asserts the refusal is the one this file is about. The id alone would read green for any other
    /// BCF1002 the same source could earn, so the reason has to name the local.
    /// </summary>
    private static Diagnostic AssertRefusedForTheLocal(string body)
    {
        var diagnostic = Assert.Single(Run(body).Diagnostics, static d => d.Id == "BCF1002");

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(
            "references local 'n' that cannot exist in generated component code",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        return diagnostic;
    }

    [Fact]
    public void IfCondition_DeclaringALocalReadFromTheThenBranch_IsAccepted()
    {
        var generated = AssertAccepted(
            """If(Take(out var n) == 0, () => Span[n.ToString()], () => Span["x"])""");

        // The declaration lands in the generated `if` header and the reference in the branch it scopes
        // over, which is the whole claim. `var` is written as its inferred type for the reason every
        // other transplanted declaration is (#342).
        Assert.Contains("if (global::C.Take(out int n) == 0)", generated, StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(2, n.ToString());", generated, StringComparison.Ordinal);
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
            "in global::C.Items(global::C.Take(out int n))", generated, StringComparison.Ordinal);
        Assert.Contains("__builder.AddContent(2, n.ToString());", generated, StringComparison.Ordinal);
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
        AssertAccepted("""Div[[..Items(Take(out var n)).Select(i => Span[n.ToString()])]]""");

    /// <summary>A nested body still reads a local from the header two constructs out.</summary>
    [Fact]
    public void IfCondition_DeclaringALocalReadFromANestedForEach_IsAccepted() =>
        AssertAccepted(
            """
            If(Take(out var n) == 0,
                () => ForEach(Items(0), i => i, i => Span[n.ToString()]),
                () => Span["x"])
            """);

    /// <summary>
    /// Both positions that normalize a body read the one check, so closing it closes both (#361). The
    /// component's own design-time expression is covered by every case above; this is the
    /// <c>[ViewPart]</c> half.
    /// </summary>
    [Fact]
    public void ViewPartBody_WithALocalDeclaredInAnIfCondition_IsAccepted()
    {
        var result = RunViewPart(
            """If(Take(out var n) == 0, () => Span[label + n.ToString()], () => Span["x"])""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id is "BCF1002" or "BCF1003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A slot's content is emitted inside a <c>RenderFragment</c> lambda of its own, so the declaration
    /// does not reach a sibling slot at all.
    /// </summary>
    [Fact]
    public void ComponentSlot_DeclaringALocalReadFromASiblingSlot_StaysRefused() =>
        AssertRefusedForTheLocal(
            """
            Component<Card>()
                .Param(c => c.Footer, Div[Take(out var n).ToString()])
                .Param(c => c.ChildContent, Div[n.ToString()])
            """);

    /// <summary>The same lambda, against a parameter that is emitted before it.</summary>
    [Fact]
    public void ComponentSlot_DeclaringALocalReadFromASiblingParameter_StaysRefused() =>
        AssertRefusedForTheLocal(
            """
            Component<Card>()
                .Param(c => c.Footer, Div[Take(out var n).ToString()])
                .Param(c => c.Title, n.ToString())
            """);

    /// <summary>
    /// An element's siblings do land in one generated block, so containment alone would admit this. They
    /// do not land in the author's order: the class channel is written ahead of the attribute loop, events
    /// and bindings after it, and a constant run of children folds into one markup frame. Containment is
    /// therefore not the test, and a sibling argument is not a header.
    /// </summary>
    [Fact]
    public void SiblingAttribute_DeclaringALocalReadFromALaterAttribute_StaysRefused() =>
        AssertRefusedForTheLocal(SiblingAttributeBody);

    /// <summary>
    /// BCF1002 names the position it is reported at. 付録A describes it as the view part diagnostic, and
    /// it is also the report for a component's own design-time expression, so calling that expression a
    /// "ViewPart method" named the wrong thing (#361).
    /// </summary>
    [Fact]
    public void DesignTimeExpression_RefusedForAnUnsupportedReference_IsNamedAsAnExpressionNotAMethod() =>
        Assert.StartsWith(
            "The Body design-time expression of 'C' is unsupported:",
            AssertRefusedForTheLocal(SiblingAttributeBody).GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    [Fact]
    public void ViewPartMethod_RefusedForAnUnsupportedReference_KeepsItsOwnWording()
    {
        var result = RunViewPart($"{SiblingAttributeBody}[label]");

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.StartsWith(
            "ViewPart method 'Row' is unsupported:",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }
}

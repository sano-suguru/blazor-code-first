using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Diagnostics on the bracket surface, verified on healthy <em>and</em> broken bodies.
/// </summary>
/// <remarks>
/// Reproducing the baselines only proves the paths that succeed.  A rewrite that silently stopped reporting
/// a diagnostic would leave every baseline green, so each diagnostic whose reporting site moves — or whose
/// channel order reverses — is asserted here directly.
/// </remarks>
public sealed class BracketSurfaceDiagnosticTests
{
    private const string CardSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Card : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter] public RenderFragment? Footer { get; set; }
            [Parameter] public object? Payload { get; set; }
        }
        """;

    /// <summary>A component with no <c>ChildContent</c> at all, so children cannot be bound to it.</summary>
    private const string PlainSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Plain : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
        }
        """;

    private static ImmutableArray<Diagnostic> Run(string body, string members = "") =>
        BracketSurfaceShim.RunGenerator(HostFiles(body, members)).Diagnostics;

    /// <summary>As <see cref="Run"/>, for a body that is deliberately not valid C#.</summary>
    private static ImmutableArray<Diagnostic> RunWithExpectedErrors(string body, string members = "") =>
        BracketSurfaceShim.RunGeneratorWithExpectedErrors(HostFiles(body, members)).Diagnostics;

    private static (string Path, string Source)[] HostFiles(string body, string members) =>
        [
            ("Host.cs", $$"""
                using System.Collections.Generic;
                using BlazorCompose;
                using static BlazorCompose.Html;

                namespace T;

                public partial class Host : ComposeComponentBase
                {
                    {{members}}
                    protected override View Body => {{body}};
                }
                """),
            ("Card.cs", CardSource),
            ("Plain.cs", PlainSource),
        ];

    [Fact]
    public void ComponentIndexer_TargetWithoutChildContent_ReportsBC3013()
    {
        var diagnostics = Run("""Component<Plain>()["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3013" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComponentIndexer_TargetWithChildContent_IsAccepted()
    {
        var diagnostics = Run("""Component<Card>()["x"]""");

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void FragmentParamThenChildren_SameSlot_ReportsBC3007()
    {
        // The indexer returns View, so .Param cannot follow the brackets and children come last. That
        // reverses which ChildContent channel is filled first: on the method surface children were always
        // syntactically first and the duplicate was caught by the .Param arm's HasBinding check, so the
        // indexer arm has to perform its own or BC3007 goes silent.
        var diagnostics = Run("""Component<Card>().Param(c => c.ChildContent, Div["y"])["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3007" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ScalarNullParamThenChildren_SameSlot_ReportsBC3007()
    {
        // `null` binds to the scalar overload (View is a struct), so this is the cross-channel case.
        var diagnostics = Run("""Component<Card>().Param(c => c.ChildContent, null)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3007");
    }

    [Fact]
    public void OtherFragmentParamThenChildren_IsAccepted()
    {
        // A different slot is not a duplicate: the indexer arm must append to the existing slots, not
        // reject them or replace them.
        var diagnostics = Run("""Component<Card>().Param(c => c.Footer, Div["f"])["x"]""");

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComponentIndexer_UnresolvedTypeArgument_ReportsBC3012Once()
    {
        var diagnostics = RunWithExpectedErrors("""Component<Missing>()["x"]""");

        Assert.Equal(1, diagnostics.Count(static d => d.Id == "BC3012"));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC1003");
    }

    [Fact]
    public void UnresolvedValueType_InsideABracketedElement_ReportsBC3015()
    {
        // The value sweep runs from the body's root. With the root an element access rather than an
        // invocation it used to return immediately, taking the whole sweep with it.
        var diagnostics = RunWithExpectedErrors(
            """Div.Class(MissingMethod() + typeof(Probe).Name)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void UnresolvedValueType_InsideABracketedChild_ReportsBC3015()
    {
        var diagnostics = RunWithExpectedErrors(
            """Div[Span.Class(MissingMethod() + typeof(Probe).Name)["x"]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void NonConstantElementTag_WithBracketedChildren_ReportsBC3009AndNotBC3015()
    {
        // Nothing is suppressed here, despite how this reads. The scanner's Element arm never reports on
        // the tag argument at all, so BC3015 has no route to it, independently of any recovery gate — and
        // independently of the constant-tag gate the sibling test below covers.
        var diagnostics = RunWithExpectedErrors("""Element(typeof(Probe).Name)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3009");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void NonConstantElementTag_WithAnUnresolvedValueInAChild_ReportsBC3009Only()
    {
        // BC3009 has already rejected the element, so the child never reaches generated code and a report
        // about it is noise. The method surface gated its child sweep on the tag being a non-empty constant;
        // deleting that arm in #87 leaves this route as the only one, so the gate moves here.
        var diagnostics = RunWithExpectedErrors(
            """Element(typeof(Probe).Name)[Span.Class(MissingMethod() + typeof(Probe).Name)["x"]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3009");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void ScalarParam_ElementBuilderValue_ReportsBC3014()
    {
        // ElementBuilder is as inert as View: the generic Param emits its value verbatim, so without this
        // the marker binds in place of content and renders silently wrong.
        var diagnostics = Run("""Component<Card>().Param(c => c.Payload, Div)""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3014" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Composable_WithAnElementBuilderParameter_IsRejected()
    {
        var diagnostics = Run(
            """Card(Span)""",
            """
            [Composable]
            private static View Card(ElementBuilder slot) => Div[slot];
            """);

        var rejection = Assert.Single(diagnostics, static d => d.Id == "BC1002");
        Assert.Contains(
            "ElementBuilder parameters are unsupported",
            rejection.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void WholeCollectionPassedInBrackets_ReportsBC1003()
    {
        var diagnostics = Run("""Div[_children]""", """private readonly View[] _children = [];""");

        Assert.Contains(diagnostics, static d => d.Id == "BC1003");
    }

    [Fact]
    public void CollectionExpressionLiteralInBrackets_ReportsBC1003()
    {
        // A nested collection-expression literal also binds non-expanded, so it is one whole collection
        // rather than two children. Brackets make the typo easier to write than the method form did; the
        // current behaviour is pinned here, not endorsed.
        var diagnostics = Run("""Div[["a", "b"]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC1003");
    }

    // ---------------------------------------------------------------------------
    // BC3008's domain: decorating something that opens no element frame
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("""Fragment("a").Class("x")""")]
    [InlineData("""Raw("<b/>").Class("x")""")]
    [InlineData("""If(true, then: () => Span["y"]).Class("x")""")]
    [InlineData("""Component<Card>().Class("x")""")]
    [InlineData("""Div["y"].Class("x")""")]
    public void DecoratingANonElement_IsCS1929AndBC1003_NotBC3008(string body)
    {
        // Every shape BC3008 was written for becomes a C# error once decorations take an ElementBuilder
        // receiver: each of these expressions is a View or a ComponentView<T>, and neither has a Class.
        AssertRetiredIntoCS1929(BracketSurfaceShim.RunGeneratorWithExpectedErrors(HostFiles(body, "")));
    }

    [Fact]
    public void DecoratingAComposableResult_IsCS1929AndBC1003_NotBC3008()
    {
        // A [Composable] method returns View, which is precisely the domain BC3008 forbids decorating.
        AssertRetiredIntoCS1929(BracketSurfaceShim.RunGeneratorWithExpectedErrors(HostFiles(
            """Card().Class("x")""",
            """
            [Composable]
            private static View Card() => Div["c"];
            """)));
    }

    /// <summary>
    /// Asserts the <em>full</em> diagnostic set for a retired BC3008 shape, not merely that CS1929 appears.
    /// </summary>
    /// <remarks>
    /// One diagnostic becomes two, and the second is the generic BC1003: once the decoration is a C# error,
    /// <c>GetSymbolInfo</c> on it yields no symbol, the analyzer's method arm returns null, and the failure is
    /// recorded as untranslatable.  BC1003 is deliberately not suppressed here: it is the only diagnostic that
    /// explains the CS0534 a missing <c>RenderView</c> raises, and exactly one BlazorCompose diagnostic has to
    /// do that.  Its <c>description</c> carries the sentence that tells the author the decoration's position,
    /// not the construct, is the problem.
    /// </remarks>
    private static void AssertRetiredIntoCS1929(GeneratorRunResult result)
    {
        Assert.Contains(BracketSurfaceShim.OutputErrors(result), static d => d.Id == "CS1929");

        string[] expected = ["BC1003"];
        Assert.Equal(
            expected,
            result.Diagnostics.Select(static d => d.Id).Distinct(StringComparer.Ordinal).ToList());
    }
}

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

    /// <summary>Runs the generator over <paramref name="body"/> and returns its diagnostics.</summary>
    /// <remarks>
    /// <para>
    /// Bodies that are deliberately not valid C# need no separate entry point.  They did while these tests
    /// ran against an in-source shim, whose own health had to be asserted before an input error could be
    /// tolerated; against the shipped runtime the compilation is built the ordinary way, and an input error
    /// is just an input error.
    /// </para>
    /// <para>
    /// What the shim's gate did carry is that no test here passes vacuously, and that is held by assertion
    /// rather than by this remark.  Each test below does one of three things: asserts a diagnostic that
    /// names a specific mistake, which cannot appear unless the analyzer reached the shape under test;
    /// asserts through <c>AssertOutputCompiles</c> that the generated output compiles, for the two accepted
    /// shapes; or, where the asserted diagnostic is the generic BC1003 fallback, asserts through
    /// <see cref="AssertOnlyRenderViewIsMissing"/> that nothing else about the input is broken.
    /// </para>
    /// </remarks>
    private static ImmutableArray<Diagnostic> Run(string body, string members = "") =>
        RunResult(body, members).Diagnostics;

    /// <summary>As <see cref="Run"/>, for the tests that need the output compilation as well.</summary>
    private static GeneratorRunResult RunResult(string body, string members = "") =>
        CompilationTestHost.RunGenerator(HostFiles(body, members));

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
        // The output compilation is asserted as well as the diagnostics: "no error was reported" is a claim
        // that would hold just as well if the body had never been analyzed at all, so on its own this test
        // could pass on an input that does not compile. The shim's own gate used to rule that out.
        var result = RunResult("""Component<Card>()["x"]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
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
        var result = RunResult("""Component<Card>().Param(c => c.Footer, Div["f"])["x"]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ComponentIndexer_UnresolvedTypeArgument_ReportsBC3012Once()
    {
        var diagnostics = Run("""Component<Missing>()["x"]""");

        Assert.Equal(1, diagnostics.Count(static d => d.Id == "BC3012"));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC1003");
    }

    [Fact]
    public void UnresolvedValueType_InsideABracketedElement_ReportsBC3015()
    {
        // The value sweep runs from the body's root. With the root an element access rather than an
        // invocation it used to return immediately, taking the whole sweep with it.
        var diagnostics = Run(
            """Div.Class(MissingMethod() + typeof(Probe).Name)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void UnresolvedValueType_InsideABracketedChild_ReportsBC3015()
    {
        var diagnostics = Run(
            """Div[Span.Class(MissingMethod() + typeof(Probe).Name)["x"]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void NonConstantElementTag_WithBracketedChildren_ReportsBC3009AndNotBC3015()
    {
        // Nothing is suppressed here, despite how this reads. The scanner's Element arm never reports on
        // the tag argument at all, so BC3015 has no route to it, independently of any recovery gate — and
        // independently of the constant-tag gate the sibling test below covers.
        var diagnostics = Run("""Element(typeof(Probe).Name)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3009");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void NonConstantElementTag_WithAnUnresolvedValueInAChild_ReportsBC3009Only()
    {
        // BC3009 has already rejected the element, so the child never reaches generated code and a report
        // about it is noise. The method surface gated its child sweep on the tag being a non-empty constant;
        // deleting that arm in #87 leaves this route as the only one, so the gate moves here.
        var diagnostics = Run(
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
        var result = RunResult("""Div[_children]""", """private readonly View[] _children = [];""");

        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
        AssertOnlyRenderViewIsMissing(result);
    }

    [Fact]
    public void CollectionExpressionLiteralInBrackets_ReportsBC1003()
    {
        // A nested collection-expression literal also binds non-expanded, so it is one whole collection
        // rather than two children. Brackets make the typo easier to write than the method form did; the
        // current behaviour is pinned here, not endorsed.
        var result = RunResult("""Div[["a", "b"]]""");

        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
        AssertOnlyRenderViewIsMissing(result);
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
    public void DecoratingANonElement_ReportsBC3008(string body)
    {
        // Each of these receivers is a View or a ComponentView<T>, and neither has a Class.
        AssertReportsBC3008(RunResult(body));
    }

    [Fact]
    public void DecoratingAComposableResult_ReportsBC3008()
    {
        // A [Composable] method returns View, which is precisely the domain BC3008 forbids decorating.
        AssertReportsBC3008(RunResult(
            """Card().Class("x")""",
            """
            [Composable]
            private static View Card() => Div["c"];
            """));
    }

    [Fact]
    public void DecoratingANonElement_ReportsBC3008_AtTheDecorationName()
    {
        // The C# error (CS1929) that would otherwise name this cannot reach the author: the host class
        // always carries CS0534 because no RenderView is generated, and csc stops after the declaration
        // stage without binding method bodies. A BlazorCompose diagnostic does get through — BC1003 did —
        // so BC3008 is what carries the explanation.
        var diagnostics = Run("""Fragment("a").Class("x")""");

        var report = Assert.Single(diagnostics, static d => d.Id == "BC3008");
        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal("Class", HostSpanText(report, """Fragment("a").Class("x")"""));
    }

    [Fact]
    public void DecorationChainOnANonElement_ReportsBC3008_OnceAtTheInnermost()
    {
        // One mistake, not three. The innermost decoration is the one whose receiver is the non-element,
        // so its span is where the chain first went wrong; everything outside it is written on the
        // ElementBuilder that Roslyn's error recovery gave the failed call, and binds cleanly.
        var diagnostics = Run("""Fragment("a").Class("x").Id("y").Title("z")""");

        var report = Assert.Single(diagnostics, static d => d.Id == "BC3008");
        Assert.Equal("Class", HostSpanText(report, """Fragment("a").Class("x").Id("y").Title("z")"""));
    }

    /// <summary>
    /// An unrelated extension method that fails to bind is not BC3008, whichever part of the shape it
    /// shares.  One case per conjunct, so each is pinned on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Other.Make().Class(1)</c> borrows the <em>name</em>: someone else's <c>.Class</c>, on a receiver
    /// and with a return type of their own.  <c>Fragment("a").Describe("x")</c> borrows the
    /// <em>receiver</em>: a method on our <c>View</c>, returning <c>string</c>.
    /// <c>Fragment("a").Wrap(1)</c> borrows the whole <em>signature</em> — our <c>View</c> in, our
    /// <c>ElementBuilder</c> out — and differs only in what it is called.
    /// </para>
    /// <para>
    /// That last case is the one that matters, and it was measured failing before the name conjunct existed:
    /// the two type tests describe a decoration's signature rather than a decoration, so a wrong-argument
    /// call to a user-declared <c>Wrap</c> was reported as a misplaced decoration, anchored at <c>Wrap</c>.
    /// An author would have been told to move attributes they never wrote.  Keep this case: it is the only
    /// one that fails if the name conjunct is dropped, the other two turning on the return type alone.
    /// </para>
    /// <para>
    /// The whole collection in brackets is what makes the body untranslatable, and it is load-bearing: the
    /// scanner runs only when translation failed, so without it none of these calls would be swept at all
    /// and the test would pass on a path it never took.  Each failed call on its own recovers to a type that
    /// converts to <c>View</c>, so each translates.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("""Div[_children, Other.Make().Class(1)]""")]
    [InlineData("""Div[_children, Fragment("a").Describe("x")]""")]
    [InlineData("""Div[_children, Fragment("a").Wrap(1)]""")]
    public void UnrelatedFailedExtension_IsNotBC3008(string body)
    {
        var result = CompilationTestHost.RunGenerator(
        [
            .. HostFiles(body, """private readonly View[] _children = [];"""),
            ("Other.cs", """
                using BlazorCompose;
                namespace T;
                public static class Other
                {
                    public sealed class Box;
                    public static Box Make() => new();
                    public static string Class(this Box box, string value) => value;
                    public static string Describe(this View view, int value) => value.ToString();
                    public static ElementBuilder Wrap(this View content, string tag) => Html.Div;
                }
                """),
        ]);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3008");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BC1003");
    }

    /// <summary>
    /// The <c>Host.cs</c> source text <paramref name="diagnostic"/> is located on, which is its report anchor.
    /// </summary>
    /// <remarks>
    /// Sliced out of the source the test supplied rather than read back through
    /// <c>Location.SourceTree</c>: a generator diagnostic crosses the symbol-free pipeline boundary as a
    /// file path and a span, so it is rebuilt as an external location and carries no tree.
    /// </remarks>
    private static string HostSpanText(Diagnostic diagnostic, string body, string members = "")
    {
        var span = diagnostic.Location.SourceSpan;

        Assert.Equal("Host.cs", diagnostic.Location.GetLineSpan().Path);

        return HostFiles(body, members)[0].Source.Substring(span.Start, span.Length);
    }

    /// <summary>
    /// Asserts the <em>full</em> diagnostic set for a misplaced decoration, not merely that BC3008 appears.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BC3008 is the only one: <c>Expand</c>'s dedup drops BC1003 once a more specific error has been
    /// recorded for the component, and BC1003 is the wrong explanation here anyway — it says the expression
    /// "uses a construct that is not statically analyzable", when the construct is analyzable and only the
    /// attributes' position is wrong.
    /// </para>
    /// <para>
    /// The CS1929 assertion is kept because it is true, and worth pinning: the type system really does reject
    /// a decoration on a <c>View</c>.  It is not evidence that the author is told so.
    /// <c>Compilation.GetDiagnostics()</c>, which is what this assertion calls, binds method bodies
    /// unconditionally; <c>csc</c> does not, and stops after the declaration stage on a compilation that has
    /// a declaration error.  A component whose design-time expression fails to translate always has one — the
    /// CS0534 from the <c>RenderView</c> that was never generated — so in a real build the CS1929 below is
    /// never computed.  That is why the in-process assertion and the fixture check different things, and why
    /// BC3008 exists rather than the C# error being left to speak: see
    /// <c>tests/diagnostic-fixtures/README.md</c>, and <c>RejectedDecorationScanner</c>'s remarks.
    /// </para>
    /// </remarks>
    private static void AssertReportsBC3008(GeneratorRunResult result)
    {
        Assert.Contains(OutputErrors(result), static d => d.Id == "CS1929");

        string[] expected = ["BC3008"];
        Assert.Equal(
            expected,
            result.Diagnostics.Select(static d => d.Id).Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Asserts that the only thing wrong with <paramref name="result"/>'s compilation is the missing
    /// <c>RenderView</c>, so a BC1003 reported against it is about the body under test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other diagnostic asserted in this file names a specific mistake, so asserting its presence
    /// establishes that the analyzer reached the shape under test.  BC1003 does not: it is the generic
    /// "uses a construct that is not statically analyzable" fallback, and an input that stopped binding for
    /// some entirely unrelated reason produces it too.  A bare <c>Assert.Contains(… BC1003)</c> would
    /// therefore keep passing on a host template that no longer says what the test means it to say.  The
    /// shim's own input gate used to rule that out; this is what replaces it.
    /// </para>
    /// <para>
    /// CS0534 is the one error left standing, and it is expected rather than tolerated: the generator is
    /// what supplies <c>RenderView</c>, and here it declined to for exactly the reason under test.  Any
    /// <em>other</em> C# error means the body failed to bind before the analyzer had an opinion about it.
    /// </para>
    /// </remarks>
    private static void AssertOnlyRenderViewIsMissing(GeneratorRunResult result)
    {
        string[] expected = ["CS0534"];
        Assert.Equal(
            expected,
            OutputErrors(result).Select(static d => d.Id).Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>Every error diagnostic the generator's output compilation reports, for assertion.</summary>
    private static ImmutableArray<Diagnostic> OutputErrors(GeneratorRunResult result) =>
        [.. result.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
}

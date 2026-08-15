using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Diagnostics on the bracket surface, verified on healthy <em>and</em> broken bodies.
/// </summary>
/// <remarks>
/// Reproducing the baselines only proves the paths that succeed. A rewrite that silently stopped reporting
/// a diagnostic would leave every baseline green, so each diagnostic whose reporting site moves, or whose
/// channel order reverses, is asserted here directly.
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
    /// Bodies that are deliberately not valid C# need no separate entry point. They did while these tests
    /// ran against an in-source shim, whose own health had to be asserted before an input error could be
    /// tolerated; against the shipped runtime the compilation is built the ordinary way, and an input error
    /// is just an input error.
    /// </para>
    /// <para>
    /// What the shim's gate did carry is that no test here passes vacuously, and that is held by assertion
    /// rather than by this remark. Each test below does one of three things: asserts a diagnostic that
    /// names a specific mistake, which cannot appear unless the analyzer reached the shape under test;
    /// asserts through <c>AssertOutputCompiles</c> that the generated output compiles, for the two accepted
    /// shapes; or, where the asserted diagnostic is the generic BCF1003 fallback, asserts through
    /// <see cref="AssertOnlyRenderViewIsMissing"/> that nothing else about the input is broken.
    /// </para>
    /// </remarks>
    private static ImmutableArray<Diagnostic> Run(string body, string members = "") =>
        RunResult(body, members).Diagnostics;

    /// <summary>As <see cref="Run"/>, for the tests that need the output compilation as well.</summary>
    private static GeneratorRunResult RunResult(string body, string members = "") =>
        CompilationTestHost.RunGenerator(HostFiles(body, members));

    /// <summary>
    /// The generated source for <paramref name="body"/>, for the cases asserted as source equality.
    /// Only the BlazorCodeFirst host produces output, <c>Card</c> and <c>Plain</c> are ordinary
    /// <c>ComponentBase</c> classes, so a single generated source is the expected shape.
    /// </summary>
    private static string GenerateSource(string body, string members = "")
    {
        var result = RunResult(body, members);
        CompilationTestHost.AssertOutputCompiles(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    private static (string Path, string Source)[] HostFiles(string body, string members) =>
        [
            ("Host.cs", $$"""
                using System.Collections.Generic;
                using System.Linq;
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace T;

                public partial class Host : BodyComponentBase
                {
                    {{members}}
                    protected override View Body => {{body}};
                }
                """),
            ("Card.cs", CardSource),
            ("Plain.cs", PlainSource),
        ];

    [Fact]
    public void ComponentIndexer_TargetWithoutChildContent_ReportsBCF3013()
    {
        var diagnostics = Run("""Component<Plain>()["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3013" && d.Severity == DiagnosticSeverity.Error);
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
    public void FragmentParamThenChildren_SameSlot_ReportsBCF3007()
    {
        // The indexer returns View, so .Param cannot follow the brackets and children come last. That
        // reverses which ChildContent channel is filled first: on the method surface children were always
        // syntactically first and the duplicate was caught by the .Param arm's HasBinding check, so the
        // indexer arm has to perform its own or BCF3007 goes silent.
        var diagnostics = Run("""Component<Card>().Param(c => c.ChildContent, Div["y"])["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3007" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ScalarNullParamThenChildren_SameSlot_ReportsBCF3007()
    {
        // `null` binds to the scalar overload (View is a struct), so this is the cross-channel case.
        var diagnostics = Run("""Component<Card>().Param(c => c.ChildContent, null)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3007");
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
    public void ComponentIndexer_UnresolvedTypeArgument_ReportsBCF3012Once()
    {
        var diagnostics = Run("""Component<Missing>()["x"]""");

        Assert.Equal(1, diagnostics.Count(static d => d.Id == "BCF3012"));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void UnresolvedValueType_InsideABracketedElement_ReportsBCF3015()
    {
        // The value sweep runs from the body's root. With the root an element access rather than an
        // invocation it used to return immediately, taking the whole sweep with it.
        var diagnostics = Run(
            """Div.Class(MissingMethod() + typeof(Probe).Name)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void UnresolvedValueType_InsideABracketedChild_ReportsBCF3015()
    {
        var diagnostics = Run(
            """Div[Span.Class(MissingMethod() + typeof(Probe).Name)["x"]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void NonConstantElementTag_WithBracketedChildren_ReportsBCF3009AndNotBCF3015()
    {
        // Nothing is suppressed here, despite how this reads. The scanner's Element arm never reports on
        // the tag argument at all, so BCF3015 has no route to it, independently of any recovery gate, and
        // independently of the constant-tag gate the sibling test below covers.
        var diagnostics = Run("""Element(typeof(Probe).Name)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3009");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void NonConstantElementTag_WithAnUnresolvedValueInAChild_ReportsBCF3009Only()
    {
        // BCF3009 has already rejected the element, so the child never reaches generated code and a report
        // about it is noise. The method surface gated its child sweep on the tag being a non-empty constant;
        // deleting that arm in #87 leaves this route as the only one, so the gate moves here.
        var diagnostics = Run(
            """Element(typeof(Probe).Name)[Span.Class(MissingMethod() + typeof(Probe).Name)["x"]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3009");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void ScalarParam_ElementViewValue_ReportsBCF3014()
    {
        // ElementView is as inert as View: the generic Param emits its value verbatim, so without this
        // the marker binds in place of content and renders silently wrong.
        var diagnostics = Run("""Component<Card>().Param(c => c.Payload, Div)""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3014" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ViewPart_WithAnElementViewParameter_IsRejected()
    {
        var diagnostics = Run(
            """Card(Span)""",
            """
            [ViewPart]
            private static View Card(ElementView slot) => Div[slot];
            """);

        var rejection = Assert.Single(diagnostics, static d => d.Id == "BCF1002");
        Assert.Contains(
            "ElementView parameters are unsupported",
            rejection.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void WholeCollectionPassedInBrackets_ReportsBCF1003()
    {
        var result = RunResult("""Div[_children]""", """private readonly View[] _children = [];""");

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        AssertOnlyRenderViewIsMissing(result);
    }

    [Fact]
    public void CollectionExpressionLiteralInBrackets_IsAcceptedAsChildren()
    {
        // Pinned as BCF1003 by #100, "the current behaviour is pinned here, not endorsed", and changed
        // deliberately by #75: the literal is the same call as Div["a", "b"] and its children are
        // present in the operation tree, so refusing it was the one shape where BCF1003's own claim of
        // "not statically analyzable" did not hold. Equivalence with the expanded spelling is asserted
        // as generated-source equality in FactoryArgumentBindingTests.
        var result = RunResult("""Div[["a", "b"]]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void SpreadOfAProjection_IsAcceptedAsChildren()
    {
        // #172: a sequence of children computed from data has a second spelling, the ordinary projection
        // spliced with `..`. It folds to the same node as ForEach with the key declined.
        var result = RunResult(
            """Ul[[.. _items.Select(i => Li[i])]]""",
            """private readonly List<string> _items = [];""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void SpreadOfAProjectionBesideSiblings_IsAcceptedAsChildren()
    {
        // What the IEnumerable<View> indexer overload considered by #172 could not do, and the reason the
        // spread is the only spelling taken: the splice mixes with children written beside it.
        var result = RunResult(
            """Ul[[Li["first"], .. _items.Select(i => Li[i]), Li["last"]]]""",
            """private readonly List<string> _items = [];""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void SpreadOfAnIndexedProjection_ReportsBCF1003()
    {
        // Select's indexed overload is not the shape #172 folds: its two-parameter lambda has no single
        // iteration variable for a content template.
        //
        // Two independent conditions refuse it, and this test does not distinguish them, which was
        // measured rather than assumed. Resolving the indexed overload into KnownSymbols instead of the
        // projection one leaves this green, because the lambda arity check refuses it; widening the
        // selector to take a wider lambda's first parameter also leaves it green, because the indexed
        // overload is a different symbol and IsEnumerableSelect refuses it. Only both mutations together
        // turn it red, so what it pins is the conjunction.
        var result = RunResult(
            """Ul[[.. _items.Select((i, n) => Li[i])]]""",
            """private readonly List<string> _items = [];""");

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        AssertOnlyRenderViewIsMissing(result);
    }

    [Fact]
    public void SpreadOfAProjectionInComponentBrackets_IsAcceptedAsChildContent()
    {
        // The splice (#172) and the component bracket channel (#329) meet here, and neither branch's
        // own tests cross them: both reach AnalyzeChildren, so a spliced child list binds to
        // ChildContent exactly as a written one does. Pinned because the two landed independently and
        // the shared path is the only thing making them agree.
        var result = RunResult(
            """Component<Card>()[[.. _items.Select(i => Span[i])]]""",
            """private readonly List<string> _items = [];""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void SplicedProjection_GeneratesSameSourceAsForEachWithTheKeyDeclined()
    {
        // The splice is sugar over ForEach with the key declined, and this is the assertion that keeps it
        // sugar rather than a second mechanism that happens to look the same today (#172).
        const string members = """private readonly List<string> _items = [];""";

        var spliced = GenerateSource("""Ul[[.. _items.Select(i => Li[i])]]""", members);
        var written = GenerateSource("""Ul[ForEach(_items, key: null, content: i => Li[i])]""", members);

        Assert.Equal(written, spliced, System.StringComparer.Ordinal);
    }

    [Fact]
    public void SpreadInsideACollectionExpressionLiteral_ReportsBCF1003()
    {
        // A spread of stored Views has no per-child written expression and is not a projection the
        // generator can fold, so it is not statically sequenceable children. #172 moved this boundary for
        // `.. source.Select(item => …)` and for nothing else: the singular Div[_view] is BCF1003 for the
        // same reason (ARCHITECTURE.md 付録A), and admitting the plural would make a sequence of stored
        // Views more permissive than one of them.
        var result = RunResult("""Div[[.._children]]""", """private readonly View[] _children = [];""");

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        AssertOnlyRenderViewIsMissing(result);
    }

    [Fact]
    public void SpreadBesideALiteralChild_ReportsBCF1003()
    {
        // The only shape that depends on child recovery being all-or-nothing. If recovery ever degrades
        // to skipping the elements it cannot resolve, the pure-spread test above keeps passing while
        // this one silently emits a div with one child instead of reporting anything.
        var result = RunResult("""Div[["a", .._children]]""", """private readonly View[] _children = [];""");

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        AssertOnlyRenderViewIsMissing(result);
    }

    [Fact]
    public void WrittenArrayCreationInBrackets_ReportsBCF1003()
    {
        // #75 accepts the collection-expression literal only. An explicitly written array creation is a
        // different shape: its elements' nearest container is the whole argument, so the recovery rule
        // cannot attribute them to individual children and every child would come out as the entire
        // array expression. It stays where it was.
        var result = RunResult("""Div[new View[] { Span["a"] }]""");

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        AssertOnlyRenderViewIsMissing(result);
    }

    [Fact]
    public void Fragment_CollectionExpressionLiteralChildren_GeneratesSameSourceAsExpanded()
    {
        // Html.Fragment is the third params ReadOnlySpan<View> surface and reads the gate at
        // RenderExpressionAnalyzer.cs:272. One shared fix has to reach it. Wrapped in a Div because
        // Fragment opens no element frame, which keeps this about children rather than root shape.
        // Non-constant children, so the two text frames stay separate: folded into markup they would
        // concatenate to "ab", which a single child spelled "ab" would produce too, and the whole point is
        // that the literal was unwrapped into two children.
        const string members = """private string _a => "a"; private string _b => "b";""";
        var literal = GenerateSource("""Div[Fragment([_a, _b])]""", members);
        var expanded = GenerateSource("""Div[Fragment(_a, _b)]""", members);

        Assert.Equal(expanded, literal);
        Assert.Contains("AddContent(1, _a)", literal);
        Assert.Contains("AddContent(2, _b)", literal);
    }

    [Fact]
    public void ComponentIndexer_CollectionExpressionLiteralChildren_GeneratesSameSourceAsExpanded()
    {
        // The component indexer reads the same gate at RenderExpressionAnalyzer.cs:620.
        // Non-constant children, for the reason the fragment case above gives.
        const string members = """private string _a => "a"; private string _b => "b";""";
        var literal = GenerateSource("""Component<Card>()[[_a, _b]]""", members);
        var expanded = GenerateSource("""Component<Card>()[_a, _b]""", members);

        Assert.Equal(expanded, literal);
        Assert.Contains("AddContent(2, _a)", literal);
        Assert.Contains("AddContent(3, _b)", literal);
    }

    [Fact]
    public void UnresolvedValueType_InsideACollectionExpressionLiteralChild_ReportsBCF3015()
    {
        // The failure-path sweep skips children whenever the params argument is unanalyzable
        // (UnresolvedValueTypeScanner.ScanChildren), so before #75 a nested literal hid everything
        // inside it: the body reported only BCF1003 and the author never learned which name could not be
        // moved into generated code. Modelled on UnresolvedValueType_InsideABracketedChild_ReportsBCF3015,
        // one bracket pair deeper.
        var diagnostics = Run("""Div[[Span.Class(MissingMethod() + typeof(Probe).Name)["x"]]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3015");
    }

    [Fact]
    public void UnresolvedValueType_BesideAnUnboundSpreadInALiteral_ReportsBCF3015()
    {
        // Reaches the scanner's syntactic binder rather than its operation-based one: an unbound spread
        // makes the whole element access an IInvalidOperation, so FactoryArguments.Bind returns null and
        // BindIndexerArguments falls back to TryBindFallback. That binder sees one argument, not two
        // children, so without the literal being unwrapped there too the sibling's unresolved type is
        // never visited and the author is left with a bare BCF1003.
        var diagnostics = Run("""Div[[Span[typeof(Probe).Name], ..MissingMethod()]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3015");
    }

    /// <summary>
    /// The unresolved type sits inside a <c>[ViewPart]</c> body that fails to translate, so only the
    /// failure-path sweep can reach it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <c>UnresolvedEmittedTypeTests.ViewPartBody_UnresolvedType_ReportsBCF3015Once</c>,
    /// whose body translates successfully: there BCF3015 comes from the success path
    /// (<c>ExpressionTemplateFactory</c> reporting through <c>ViewPartBodyContext</c>) and
    /// <c>UnresolvedValueTypeScanner</c> is never reached. Measured by deleting the scanner call from
    /// <c>ViewPartDefinitionFactory</c>: that test still passed, and so did every other behavioural test
    /// in the suite, only the structural wiring guard failed. This case is the one that fails, and that
    /// is the whole reason it exists.
    /// </para>
    /// <para>
    /// The unbound spread is what forces the failure path, for the reason spelled out in
    /// <see cref="UnresolvedValueType_BesideAnUnboundSpreadInALiteral_ReportsBCF3015"/>; here it is written
    /// inside the <c>[ViewPart]</c> body rather than in <c>Body</c>, so the sweep that finds it runs from
    /// the other design-time-expression host.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnresolvedValueType_InsideAViewPartBody_ReportsBCF3015()
    {
        var diagnostics = Run(
            """Div["ok"]""",
            """
            [ViewPart]
            private static View Broken() => Div[[Span[typeof(Probe).Name], ..MissingMethod()]];
            """);

        Assert.Contains(diagnostics, static d => d.Id == "BCF3015");
    }

    // ---------------------------------------------------------------------------
    // BCF3008's domain: decorating something that opens no element frame
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("""Fragment("a").Class("x")""")]
    [InlineData("""Raw("<b/>").Class("x")""")]
    [InlineData("""If(true, then: () => Span["y"]).Class("x")""")]
    [InlineData("""Component<Card>().Class("x")""")]
    [InlineData("""Div["y"].Class("x")""")]
    public void DecoratingANonElement_ReportsBCF3008(string body)
    {
        // Each of these receivers is a View or a ComponentView<T>, and neither has a Class.
        AssertReportsBCF3008(RunResult(body));
    }

    [Fact]
    public void DecoratingAViewPartResult_ReportsBCF3008()
    {
        // A [ViewPart] method returns View, which is precisely the domain BCF3008 forbids decorating.
        AssertReportsBCF3008(RunResult(
            """Card().Class("x")""",
            """
            [ViewPart]
            private static View Card() => Div["c"];
            """));
    }

    /// <summary>
    /// An externally supplied <c>RenderFragment</c> is a receiver BCF3008 covers, not a gap in it.
    /// </summary>
    /// <remarks>
    /// The receiver has to be named explicitly in <c>RejectedDecorationScanner</c>, and this case is what
    /// pins that: <c>RenderFragment</c> converts to <c>View</c>, but an extension-method receiver admits only
    /// identity, reference and boxing conversions, so the conversion is not applied when resolving
    /// <c>.Class</c> and a receiver test against <c>View</c> alone never matches. Before that clause existed
    /// this input reported BCF1003, the generic "not statically analyzable" fallback, while
    /// <c>DESIGN.md</c> §4.1 stated it was BCF3008, grouping a supplied <c>RenderFragment</c> with
    /// <c>Fragment</c> and <c>Raw</c> as content that opens no element frame.
    /// </remarks>
    [Fact]
    public void DecoratingASuppliedRenderFragment_ReportsBCF3008()
    {
        AssertReportsBCF3008(RunResult(
            """Div[Slot.Class("x")]""",
            """
            [Microsoft.AspNetCore.Components.Parameter]
            public Microsoft.AspNetCore.Components.RenderFragment? Slot { get; set; }
            """));
    }

    /// <summary>
    /// The misplaced decoration is written <em>inside</em> a <c>[ViewPart]</c> body rather than in
    /// <c>Body</c>, so the sweep that finds it runs from the other design-time-expression host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="DecoratingAViewPartResult_ReportsBCF3008"/>, which decorates what a
    /// view part <em>returns</em> and is written in <c>Body</c> like every other case in this group. Here
    /// the view part's own body is the broken expression, and <c>Body</c> is healthy.
    /// </para>
    /// <para>
    /// Kept as its own case because the shape is not covered by any other: a design-time expression has two
    /// hosts, <c>ComponentModelFactory</c> for <c>Body</c> and <c>ViewPartDefinitionFactory</c> for
    /// <c>[ViewPart]</c>, and each wires its own failure-path sweeps. BCF3008 was reported from inside
    /// <c>RenderExpressionAnalyzer.Classify</c>, which both hosts route through, until it moved to a
    /// caller-invoked scanner; the view part host was then left without it, and every existing case here
    /// shares the <c>Body</c> host template and so kept passing. Measured on that state, this input reported
    /// BCF1002 alone, "body must be a statically sequenceable expression", the generic text BCF3008 exists to
    /// displace. <c>FailurePathScannerParityTests</c> guards the wiring; this guards the author-facing
    /// result.
    /// </para>
    /// </remarks>
    [Fact]
    public void DecoratingANonElement_InsideAViewPartBody_ReportsBCF3008()
    {
        AssertReportsBCF3008(RunResult(
            """Div["ok"]""",
            """
            [ViewPart]
            private static View Card() => Fragment("a").Class("x");
            """));
    }

    [Fact]
    public void DecoratingANonElement_ReportsBCF3008_AtTheDecorationName()
    {
        // The C# error (CS1929) that would otherwise name this cannot reach the author: the host class
        // always carries CS0534 because no RenderView is generated, and csc stops after the declaration
        // stage without binding method bodies. A BlazorCodeFirst diagnostic does get through, BCF1003 did,
        // so BCF3008 is what carries the explanation.
        var diagnostics = Run("""Fragment("a").Class("x")""");

        var report = Assert.Single(diagnostics, static d => d.Id == "BCF3008");
        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal("Class", HostSpanText(report, """Fragment("a").Class("x")"""));
    }

    [Fact]
    public void DecorationChainOnANonElement_ReportsBCF3008_OnceAtTheInnermost()
    {
        // One mistake, not three. The innermost decoration is the one whose receiver is the non-element,
        // so its span is where the chain first went wrong; everything outside it is written on the
        // ElementView that Roslyn's error recovery gave the failed call, and binds cleanly.
        var diagnostics = Run("""Fragment("a").Class("x").Id("y").Title("z")""");

        var report = Assert.Single(diagnostics, static d => d.Id == "BCF3008");
        Assert.Equal("Class", HostSpanText(report, """Fragment("a").Class("x").Id("y").Title("z")"""));
    }

    /// <summary>
    /// An unrelated extension method that fails to bind is not BCF3008, whichever part of the shape it
    /// shares. Four cases, one per part of the shape a decoration has that an unrelated method can borrow
    /// on its own, name, receiver, and each of the two ways of borrowing every part but one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not one case per conjunct: the four cover the parts of the shape an unrelated method can borrow, and
    /// only the last two turn on the conjunct each of them borrows, as the second paragraph records. The
    /// name conjunct is pinned by <c>Fragment("a").Wrap(1)</c>; the receiver conjunct, by
    /// <c>Other.MakeBin().Id(1)</c> below, the receiver clause could otherwise be widened without any case
    /// here noticing, which is what happened when <c>RenderFragment</c> was added to it. The return type
    /// conjunct has no case that pins it the same way; nothing here claims otherwise.
    /// </para>
    /// <para>
    /// <c>Other.Make().Class(1)</c> borrows the <em>name</em>: someone else's <c>.Class</c>, on a receiver
    /// and with a return type of their own. <c>Fragment("a").Describe("x")</c> borrows the
    /// <em>receiver</em>: a method on our <c>View</c>, returning <c>string</c>.
    /// <c>Fragment("a").Wrap(1)</c> borrows the whole <em>signature</em> but the name, our <c>View</c> in,
    /// our <c>ElementView</c> out, and differs only in what it is called.
    /// <c>Other.MakeBin().Id(1)</c> borrows the whole signature but the <em>receiver</em>, a genuine
    /// decoration name, returning our <c>ElementView</c>, and differs only in what it is written on. It
    /// is deliberately named <c>.Id</c> rather than <c>.Class</c>: a second unrelated <c>.Class</c> extension
    /// in the same compilation collides with <c>Other.Make().Class(1)</c>'s own, and Roslyn's error recovery
    /// stops offering a candidate return type for either call once the name is ambiguous, which would have
    /// made this case indistinguishable from a return-type failure instead of a receiver-only one. No other
    /// case here declares an <c>.Id</c>, so recovery stays exact.
    /// </para>
    /// <para>
    /// The last two cases are the ones that matter, and the first of them, <c>Wrap</c>, was measured
    /// failing before the name conjunct existed: the two type tests describe a decoration's signature rather
    /// than a decoration, so a wrong-argument call to a user-declared <c>Wrap</c> was reported as a misplaced
    /// decoration, anchored at <c>Wrap</c>. An author would have been told to move attributes they never
    /// wrote. Keep this case: it is the only one that fails if the name conjunct is dropped, the other three
    /// turning on the return type or the receiver instead. <c>Other.MakeBin().Id(1)</c> is its mirror for
    /// the receiver conjunct: it is the only one that fails if the receiver conjunct is dropped, and its
    /// removal was verified to flip this case to BCF3008 while every other case here is unaffected.
    /// </para>
    /// <para>
    /// The whole collection in brackets is what makes the body untranslatable, and it is load-bearing: the
    /// scanner runs only when translation failed, so without it none of these calls would be swept at all
    /// and the test would pass on a path it never took. Each failed call on its own recovers to a type that
    /// converts to <c>View</c>, so each translates.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("""Div[_children, Other.Make().Class(1)]""")]
    [InlineData("""Div[_children, Fragment("a").Describe("x")]""")]
    [InlineData("""Div[_children, Fragment("a").Wrap(1)]""")]
    [InlineData("""Div[_children, Other.MakeBin().Id(1)]""")]
    public void UnrelatedFailedExtension_IsNotBCF3008(string body)
    {
        var result = CompilationTestHost.RunGenerator(
        [
            .. HostFiles(body, """private readonly View[] _children = [];"""),
            ("Other.cs", """
                using BlazorCodeFirst;
                namespace T;
                public static class Other
                {
                    public sealed class Box;
                    public static Box Make() => new();
                    public static string Class(this Box box, string value) => value;
                    public static string Describe(this View view, int value) => value.ToString();
                    public static ElementView Wrap(this View content, string tag) => Html.Div;

                    public sealed class Bin;
                    public static Bin MakeBin() => new();
                    public static ElementView Id(this Bin bin, string value) => Html.Div;
                }
                """),
        ]);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3008");
        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
    }

    // ---------------------------------------------------------------------------
    // BCF3026's domain: a decoration name the runtime does not declare
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A misspelled decoration. Nothing declares <c>Clas</c>, so the call binds to no method at all, and
    /// CS1061 is the C# error that would name the misspelling.
    /// </summary>
    /// <remarks>
    /// That error never reaches the author, for the reason <see cref="AssertReportsBCF3008"/>'s remarks
    /// record about CS1929: the host class carries CS0534 because no <c>RenderView</c> was generated, and
    /// <c>csc</c> stops after the declaration stage without binding method bodies. What the author got
    /// instead was BCF1003, which says the expression "uses a construct that is not statically analyzable"
    /// while the receiver opens an element frame and the children are ordinary (#241).
    /// </remarks>
    [Fact]
    public void MisspelledDecoration_ReportsBCF3026_AtTheDecorationName()
    {
        var diagnostics = Run("""Div.Clas("card")["hi"]""");

        var report = Assert.Single(diagnostics, static d => d.Id == "BCF3026");
        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal("Clas", HostSpanText(report, """Div.Clas("card")["hi"]"""));
        Assert.Contains("Clas", report.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// A name the runtime does declare, on a receiver that opens no element frame, stays BCF3008.
    /// </summary>
    /// <remarks>
    /// The two conditions share one sweep and are disjoint by construction, since BCF3008 requires
    /// <c>DeclaresDecorationNamed</c> to hold and BCF3026 requires it to fail. This is what holds them
    /// apart if that sweep is ever rewritten.
    /// </remarks>
    [Fact]
    public void DecoratingANonElementWithADeclaredName_StaysBCF3008()
    {
        var diagnostics = Run("""Fragment("a").Class("x")""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3008");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3026");
    }

    /// <summary>
    /// An extension method the consumer declared on <c>ElementView</c>. It binds, so there is no C# error at
    /// all here, and the only diagnostic on the line was BCF1003 (#241).
    /// </summary>
    /// <remarks>
    /// Run through <see cref="CompilationTestHost.RunGenerator(ValueTuple{string, string}[])"/> rather than
    /// through <see cref="HostFiles"/>, because the extension has to be declared in a static class of its own
    /// and <c>members</c> is spliced into the component's body. Adding a fourth file to
    /// <see cref="HostFiles"/> would change the input of every test in this class.
    /// </remarks>
    [Fact]
    public void UnrecognizedElementExtension_ReportsBCF3026()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace T;

                public static class Hx
                {
                    public static ElementView HxGet(this ElementView element, string url) => element;
                }

                public partial class Host : BodyComponentBase
                {
                    protected override View Body => Div.HxGet("/x")["hi"];
                }
                """));

        Assert.Contains(
            result.Diagnostics,
            static d => d.Id == "BCF3026" && d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>
    /// The same shape returning <c>View</c> is not reported, and keeps BCF1003.
    /// </summary>
    /// <remarks>
    /// The return-type conjunct is what keeps ordinary calls on an element out of this diagnostic:
    /// <c>Div.ToString()</c> has the same receiver and the same unrecognized name, and only the return type
    /// tells the two apart. A method that returns <c>View</c> wraps rather than decorates, so it is left
    /// outside on purpose; that is the residue recorded on #241 and not an oversight.
    /// </remarks>
    [Fact]
    public void ElementExtensionReturningView_StaysBCF1003()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace T;

                public static class Wrapper
                {
                    public static View Wrap(this ElementView element) => default;
                }

                public partial class Host : BodyComponentBase
                {
                    protected override View Body => Div.Wrap();
                }
                """));

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3026");
    }

    // ---------------------------------------------------------------------------
    // BCF3027's domain: a declaration of the author's own taking a curated element helper's name
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A member whose name is a curated helper's wins simple-name lookup over the imported property, so the
    /// element expression becomes an indexer call on that member.
    /// </summary>
    /// <remarks>
    /// The C# error is CS1503, which names neither the element, nor the member, nor the fix, and which the
    /// declaration-stage cutoff keeps from the author anyway (#127). What arrived instead was BCF1003's
    /// "not statically analyzable", said of an expression that is perfectly analyzable and merely bound to
    /// the wrong thing.
    /// </remarks>
    [Fact]
    public void MemberShadowingAnElementHelper_ReportsBCF3027_AtTheShadowedReceiver()
    {
        var diagnostics = Run("""Div[Data["Heading"]]""", """private string Data => "";""");

        var report = Assert.Single(diagnostics, static d => d.Id == "BCF3027");
        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal(
            "Data",
            HostSpanText(report, """Div[Data["Heading"]]""", """private string Data => "";"""));
        var message = report.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Data", message, StringComparison.Ordinal);
        Assert.Contains("is a member", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same name written unqualified, with nothing shadowing it, is the ordinary element and must not be
    /// blamed for a failure elsewhere in the body.
    /// </summary>
    /// <remarks>
    /// The body fails for an unrelated reason (a child list handed over whole, BCF1003), which is what puts
    /// this sweep on the failure path at all. Without the identity conjunct against the resolved helper
    /// table every unqualified curated name in a broken body would be reported as shadowed.
    /// </remarks>
    [Fact]
    public void UnshadowedElementHelper_IsNotBCF3027()
    {
        var diagnostics = Run("""Div[Data["Heading"], _kids]""", """private readonly View[] _kids = [];""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3027");
    }

    /// <summary>
    /// Two static imports declaring the same name leave the lookup ambiguous rather than shadowed, and that
    /// is not this report.
    /// </summary>
    /// <remarks>
    /// The scanner reads a candidate only where there is exactly one, so this shape falls to BCF1003. What
    /// it cannot do is name the declaration that took the name, because none of them did: one of the two
    /// candidates here is the element helper itself, and "a member declared outside BlazorCodeFirst.Html"
    /// would be a false description of a compilation where the helper is in scope and reachable.
    /// <para>
    /// The source is hand-rolled rather than taken from <see cref="HostFiles"/> because the second
    /// <c>using static</c> that creates the ambiguity has to sit in the file header, which that helper
    /// owns and does not parameterize.
    /// </para>
    /// </remarks>
    [Fact]
    public void AmbiguousElementHelperName_IsNotBCF3027()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;
                using static T.Mine;

                namespace T;

                public static class Mine
                {
                    public static string Data => "";
                }

                public partial class Host : BodyComponentBase
                {
                    protected override View Body => Div[Data["Heading"]];
                }
                """));

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3027");
    }

    /// <summary>The documented fix, qualifying the element, is not itself reported.</summary>
    /// <remarks>
    /// <c>Html.Data</c> is a member access rather than a simple name, so no lookup was shadowed. The body
    /// still fails, for the same unrelated reason as above, so the sweep does run over this expression.
    /// </remarks>
    [Fact]
    public void QualifiedElementHelper_IsNotBCF3027()
    {
        var diagnostics = Run(
            """Div[Html.Data["Heading"], _kids]""",
            """private readonly string Data = ""; private readonly View[] _kids = [];""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3027");
    }

    /// <summary>
    /// A <em>type</em> of the author's own takes the name the same way a member does, and is the same
    /// report, naming the type (#266).
    /// </summary>
    /// <remarks>
    /// #127 left the type case out because C# raises CS0119 for it, which does name the shadowing
    /// declaration. What #266 measured is that it never arrives: CS0119 is a body-binding error, so the
    /// declaration-stage cutoff suppresses it exactly as it suppresses the member case's CS1503, and the
    /// author was left with BCF1003 alone. BCF1003 is gone from the set below because <c>Expand</c>'s dedup
    /// drops it once a specific error has been recorded.
    /// </remarks>
    [Fact]
    public void TypeShadowingAnElementHelper_ReportsBCF3027_NamingItAsAType()
    {
        var diagnostics = Run("""Table["x"]""", "public sealed class Table;");

        var report = Assert.Single(diagnostics, static d => d.Id == "BCF3027");
        Assert.Equal("Table", HostSpanText(report, """Table["x"]""", "public sealed class Table;"));
        Assert.Contains("is a type", report.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF1003");
    }

    /// <summary>
    /// A <em>method</em> of the author's own takes the name too, and C# has a third error for it, CS0021 on
    /// the method group. The cutoff suppresses that one as well, so it is the same report (#266).
    /// </summary>
    [Fact]
    public void MethodShadowingAnElementHelper_ReportsBCF3027_NamingItAsAMethod()
    {
        var diagnostics = Run("""Summary["x"]""", """private string Summary() => "";""");

        var report = Assert.Single(diagnostics, static d => d.Id == "BCF3027");
        Assert.Contains(
            "is a method", report.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// A <em>namespace</em> is the fourth shape, and the one that needs no declaration inside the component
    /// at all: a project with a <c>T.Article</c> namespace takes <c>Article</c> from every component in
    /// <c>T</c> (#266).
    /// </summary>
    [Fact]
    public void NamespaceShadowingAnElementHelper_ReportsBCF3027_NamingItAsANamespace()
    {
        var result = CompilationTestHost.RunGenerator(
        [
            .. HostFiles("""Article["x"]""", members: ""),
            ("Article.cs", "namespace T.Article { }"),
        ]);

        var report = Assert.Single(result.Diagnostics, static d => d.Id == "BCF3027");
        Assert.Equal("Article", HostSpanText(report, """Article["x"]"""));
        Assert.Contains(
            "is a namespace", report.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
    /// Asserts the <em>full</em> diagnostic set for a misplaced decoration, not merely that BCF3008 appears.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BCF3008 is the only one: <c>Expand</c>'s dedup drops BCF1003 once a more specific error has been
    /// recorded for the component, and BCF1003 is the wrong explanation here anyway: it says the expression
    /// "uses a construct that is not statically analyzable", when the construct is analyzable and only the
    /// attributes' position is wrong.
    /// </para>
    /// <para>
    /// The CS1929 assertion is kept because it is true, and worth pinning: the type system really does reject
    /// a decoration on a <c>View</c>. It is not evidence that the author is told so.
    /// <c>Compilation.GetDiagnostics()</c>, which is what this assertion calls, binds method bodies
    /// unconditionally; <c>csc</c> does not, and stops after the declaration stage on a compilation that has
    /// a declaration error. A component whose design-time expression fails to translate always has one, the
    /// CS0534 from the <c>RenderView</c> that was never generated, so in a real build the CS1929 below is
    /// never computed. That is why the in-process assertion and the fixture check different things, and why
    /// BCF3008 exists rather than the C# error being left to speak: see
    /// <c>tests/diagnostic-fixtures/README.md</c>, and <c>RejectedDecorationScanner</c>'s remarks.
    /// </para>
    /// </remarks>
    private static void AssertReportsBCF3008(GeneratorRunResult result)
    {
        Assert.Contains(OutputErrors(result), static d => d.Id == "CS1929");

        string[] expected = ["BCF3008"];
        Assert.Equal(
            expected,
            result.Diagnostics.Select(static d => d.Id).Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Asserts that the only thing wrong with <paramref name="result"/>'s compilation is the missing
    /// <c>RenderView</c>, so a BCF1003 reported against it is about the body under test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other diagnostic asserted in this file names a specific mistake, so asserting its presence
    /// establishes that the analyzer reached the shape under test. BCF1003 does not: it is the generic
    /// "uses a construct that is not statically analyzable" fallback, and an input that stopped binding for
    /// some entirely unrelated reason produces it too. A bare <c>Assert.Contains(… BCF1003)</c> would
    /// therefore keep passing on a host template that no longer says what the test means it to say. The
    /// shim's own input gate used to rule that out; this is what replaces it.
    /// </para>
    /// <para>
    /// CS0534 is the one error left standing, and it is expected rather than tolerated: the generator is
    /// what supplies <c>RenderView</c>, and here it declined to for exactly the reason under test. Any
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

    /// <summary>A handler for the modifier tests below, which are about placement and not about events.</summary>
    private const string WheelMembers = "private void Zoom() { }";

    [Fact]
    public void PreventDefault_WithNoEventBeforeIt_ReportsBCF3035()
    {
        var diagnostics = Run("""Div.Class("x").PreventDefault()""", WheelMembers);

        var reported = Assert.Single(diagnostics, static d => d.Id == "BCF3035");
        Assert.Contains("PreventDefault", reported.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Written before the event it was meant for, which is the shape the chain makes easy to reach.
    /// </summary>
    /// <remarks>
    /// The event exists on this element, so a rule that merely counted the element's events would let this
    /// through. What decides it is that the modifier attaches to what precedes it, and nothing does.
    /// </remarks>
    [Fact]
    public void PreventDefault_BeforeTheEventItWasMeantFor_ReportsBCF3035()
    {
        var diagnostics = Run("""Div.PreventDefault().On("onwheel", Zoom)""", WheelMembers);

        Assert.Single(diagnostics, static d => d.Id == "BCF3035");
    }

    [Fact]
    public void PreventDefault_Twice_ReportsBCF3036()
    {
        var diagnostics = Run("""Div.On("onwheel", Zoom).PreventDefault().PreventDefault()""", WheelMembers);

        var reported = Assert.Single(diagnostics, static d => d.Id == "BCF3036");
        Assert.Contains("onwheel", reported.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// The two modifiers are separate channels, so writing both on one event is not a duplicate.
    /// </summary>
    /// <remarks>
    /// This is what keeps BCF3036 from degenerating into "reject a second modifier of any kind", which a
    /// rule written against the count of modifiers rather than against the channel would do.
    /// </remarks>
    [Fact]
    public void PreventDefaultAndStopPropagation_OnOneEvent_ReportNothing()
    {
        var diagnostics = Run("""Div.On("onwheel", Zoom).PreventDefault().StopPropagation()""", WheelMembers);

        Assert.DoesNotContain(diagnostics, static d => d.Id is "BCF3035" or "BCF3036");
    }
}

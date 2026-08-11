using System.Linq;
using System.Threading.Tasks;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Tests for <see cref="InertSurfaceAnalyzer"/> (BCF3029): the design-time API written where no design-time
/// expression reads it, so it builds an empty marker and renders nothing (#68).
/// </summary>
/// <remarks>
/// <para>
/// The two theory sets carry the shapes the firing condition is stated in, and the individual facts below
/// them carry the ones whose <em>count</em> or whose reason is the claim: one report per written chain, and
/// the three exemptions (an inert-returning declaration, storage into an inert member, a declaration of the
/// author's own that is not part of the API).
/// </para>
/// <para>
/// No source pairs an expression-bodied getter against a block-bodied one. The analyzer never sees an
/// accessor's syntax: both spellings are analyzed under the same <c>get_Body</c> symbol, so the exemption
/// comes from the same line either way and a second source could only fail alongside the first. BCF3001 does
/// carry that pair, because there the spelling once mattered (#220 replaced a syntax walk).
/// </para>
/// </remarks>
public sealed class InertSurfaceAnalyzerTests
{
    // -----------------------------------------------------------------------
    // Sources that must report BCF3029
    // -----------------------------------------------------------------------

    /// <summary>#68's motivating example: a chain written in an ordinary void method.</summary>
    private const string ChainInVoidMethodSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div["ok"];

            private void OnSomething()
            {
                var v = Html.Div.Class("card").OnClick(DoThing)[Html.Span["hello"]];
            }

            private void DoThing() { }
        }
        """;

    /// <summary>
    /// A bare childless element: an <c>ElementView</c> with no invocation and no brackets anywhere in it, so
    /// this is the only source whose report comes from a plain <c>OperationKind.PropertyReference</c>. #68
    /// named it as the shape with nothing to key on, and it is the one that holds that half of the
    /// registration.
    /// </summary>
    private const string ChildlessElementInVoidMethodSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div["ok"];

            private void Warm()
            {
                var v = Html.Div;
            }
        }
        """;

    /// <summary>A discard, which #68 names alongside the local: both render nothing.</summary>
    private const string DiscardInVoidMethodSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div["ok"];

            private void Warm()
            {
                _ = Html.Div["hello"];
            }
        }
        """;

    /// <summary>A structural helper rather than an element: If produces a View just the same.</summary>
    private const string StructuralHelperInVoidMethodSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private bool _flag;

            protected override View Body => Html.Div["ok"];

            private void Warm()
            {
                var v = Html.If(_flag, () => Html.Span["hello"]);
            }
        }
        """;

    /// <summary>A component reference, whose parameters would never be applied.</summary>
    private const string ComponentInVoidMethodSource = """
        using BlazorCodeFirst;

        public partial class Widget : BodyComponentBase
        {
            protected override View Body => Html.Span["w"];
        }

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div["ok"];

            private void Warm()
            {
                var v = Html.Component<Widget>();
            }
        }
        """;

    /// <summary>
    /// A <c>[ViewPart]</c> call, which expands only where a design-time expression reads it. The attribute is
    /// what separates this from the Opaque path, so it is reported where a bare <c>View</c>-returning method
    /// is not.
    /// </summary>
    private const string ViewPartCallInVoidMethodSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            [ViewPart] private static View Card(string title) => Div.Class("card")[H2[title]];

            protected override View Body => Card("ok");

            private void Warm()
            {
                var v = Card("hello");
            }
        }
        """;

    /// <summary>
    /// The shape the issue's example actually sits in: an event handler. The enclosing lambda returns
    /// <c>void</c>, so the inert <c>OnClick</c> call wrapping it exempts nothing inside it.
    /// </summary>
    private const string ChainInsideAnEventHandlerLambdaSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            protected override View Body =>
                Button.OnClick(() => { var v = Div.Class("card")[Span["hello"]]; })["Go"];
        }
        """;

    /// <summary>Storage that is not into an inert member: nothing can read a View back out of an object.</summary>
    private const string StoredInObjectFieldSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private object _cached = Div.Class("card");

            protected override View Body => Div["ok"];
        }
        """;

    /// <summary>
    /// The two bracketed child lists that are not <c>ElementView</c>'s: a component's and a content-taking
    /// part's. Each is a distinct indexer symbol resolved out of the runtime, so each is its own arm of
    /// <c>KnownSymbols.IsDesignTimeApiMember</c> and neither is covered by the element case.
    /// </summary>
    private const string BracketedComponentAndSlotOutsideABodySource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Widget : BodyComponentBase
        {
            protected override View Body => Span["w"];
        }

        public partial class C : BodyComponentBase
        {
            [ViewPart] private static SlotView Panel(string title) => Div.Class("panel")[H2[title], Slot];

            protected override View Body => Div["ok"];

            private void Warm()
            {
                var component = Component<Widget>()[Span["child"]];
                var part = Panel("hello")[P["body"]];
            }
        }
        """;

    /// <summary>Passed to an ordinary runtime method, which receives the empty marker.</summary>
    private const string PassedToARuntimeMethodSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Div["ok"];

            private void Warm() => Consume(Div.Class("card")[Span["hello"]]);

            private static void Consume(View view) { }
        }
        """;

    // -----------------------------------------------------------------------
    // Sources that must NOT report BCF3029
    // -----------------------------------------------------------------------

    private const string BodySource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private bool _flag;

            protected override View Body =>
                Div.Class("card").OnClick(() => { })[If(_flag, () => Span["hello"]), Fragment(P["p"])];
        }
        """;

    private const string ChromeSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Shell : ChromeLayoutBase
        {
            protected override View Chrome => Main.Class("shell")[Body];
        }
        """;

    private const string ViewPartDeclarationSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            [ViewPart] private static View Card(string title) => Div.Class("card")[H2[title]];

            protected override View Body => Card("ok");
        }
        """;

    private const string ContentTakingViewPartDeclarationSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            [ViewPart] private static SlotView Panel(string title) =>
                Div.Class("panel")[H2[title], Slot];

            protected override View Body => Panel("ok")[P["body"]];
        }
        """;

    /// <summary>
    /// The ForEach content lambda, whose declaration returns View. Condition 1 exempts it without any
    /// position being named, which is the point of asking a return type rather than keeping a list.
    /// </summary>
    private const string ForEachContentLambdaSource = """
        using System.Collections.Generic;
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private List<string> _items = new();

            protected override View Body => Ul[ForEach(_items, i => i, i => Li.Class("row")[i])];
        }
        """;

    /// <summary>Storage into an inert-typed field, which ARCHITECTURE.md §2.3 leaves unreserved.</summary>
    private const string StoredInInertFieldSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private View _cached;

            protected override View Body => _cached;

            private void Warm()
            {
                _cached = Div.Class("card")[Span["hello"]];
            }
        }
        """;

    /// <summary>The same storage written as an initializer, which no return type could exempt.</summary>
    private const string InertFieldInitializerSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private View _cached = Div.Class("card")[Span["hello"]];

            protected override View Body => _cached;
        }
        """;

    /// <summary>
    /// The property side of the initializer exemption, which nothing else reaches: a get-only property with
    /// an initializer is an <c>IPropertyInitializerOperation</c>, its own arm of the storage test.
    /// </summary>
    private const string InertPropertyInitializerSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private ElementView Cached { get; } = Div.Class("card");

            protected override View Body => Cached["hello"];
        }
        """;

    private const string StoredInInertPropertySource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private ElementView Cached { get; set; }

            protected override View Body => Cached["hello"];

            private void Warm()
            {
                Cached = Div.Class("card");
            }
        }
        """;

    /// <summary>
    /// The Opaque reservation (DESIGN.md §5.3, 付録B.11(b)): a View-returning declaration of the author's own
    /// carries no [ViewPart], is not part of the design-time API, and is reported neither where it is
    /// declared nor where it is called. The forgotten attribute is BCF2001's to answer at Info (#260).
    /// </summary>
    private const string OpaqueViewReturningMethodSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private static View Card(string title) => Div.Class("card")[H2[title]];

            protected override View Body => Div["ok"];

            private void Warm()
            {
                var v = Card("hello");
            }
        }
        """;

    /// <summary>
    /// Reading a stored inert value is not a reference to the API and is not reported. Without this the
    /// exemption above would be worth nothing: every read of the cache it permits would be an error.
    /// </summary>
    private const string ReadingStoredInertValuesSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private View _cached;

            private View Cached => _cached;

            protected override View Body => _cached;

            private void Warm()
            {
                var v = _cached;
                var w = Cached;
                Consume(v);
            }

            private static void Consume(View view) { }
        }
        """;

    // -----------------------------------------------------------------------
    // Theory data
    // -----------------------------------------------------------------------

    /// <remarks>
    /// <c>ChainInVoidMethodSource</c> is deliberately absent: the count and the span are both claimed of it
    /// by a dedicated fact below, and a third <c>Assert.Contains</c> over the same source would be one more
    /// place to keep in step for no additional coverage.
    /// </remarks>
    public static TheoryData<string> SourcesThatReportBCF3029 { get; } = new(
        ChildlessElementInVoidMethodSource,
        DiscardInVoidMethodSource,
        StructuralHelperInVoidMethodSource,
        ComponentInVoidMethodSource,
        ViewPartCallInVoidMethodSource,
        ChainInsideAnEventHandlerLambdaSource,
        StoredInObjectFieldSource,
        PassedToARuntimeMethodSource);

    public static TheoryData<string> SourcesThatDoNotReportBCF3029 { get; } = new(
        BodySource,
        ChromeSource,
        ViewPartDeclarationSource,
        ContentTakingViewPartDeclarationSource,
        ForEachContentLambdaSource,
        StoredInInertFieldSource,
        InertFieldInitializerSource,
        InertPropertyInitializerSource,
        StoredInInertPropertySource,
        OpaqueViewReturningMethodSource,
        ReadingStoredInertValuesSource);

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(SourcesThatReportBCF3029))]
    public async Task InertSurfaceAnalyzer_SurfaceOutsideADesignTimeExpression_ReportsBCF3029(string source)
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(source);

        Assert.Contains(diagnostics, static d => d.Id == "BCF3029" && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(SourcesThatDoNotReportBCF3029))]
    public async Task InertSurfaceAnalyzer_SurfaceAReaderWouldRead_DoesNotReportBCF3029(string source)
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3029");
    }

    /// <summary>
    /// One report per written chain, and it covers the whole chain rather than its innermost link. The
    /// motivating example holds five references to the API — the two helpers, the two decorations, and the
    /// bracketed child list — and an author who mistyped one position made one mistake; what is wrong is the
    /// position of the expression rather than anything inside it, so the span is the expression they have to
    /// move.
    /// </summary>
    [Fact]
    public async Task InertSurfaceAnalyzer_AChainOfSurfaceCalls_ReportsBCF3029OnceAtTheWholeExpression()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(
            ChainInVoidMethodSource);

        var diagnostic = Assert.Single(diagnostics, static d => d.Id == "BCF3029");
        var span = diagnostic.Location.SourceSpan;
        var text = diagnostic.Location.SourceTree!.GetText().ToString(span);

        Assert.Equal("Html.Div.Class(\"card\").OnClick(DoThing)[Html.Span[\"hello\"]]", text);
    }

    /// <summary>
    /// Two independent mistakes in one method are two reports. The de-duplication above is about one chain,
    /// not about one declaration, so it must not swallow the second.
    /// </summary>
    [Fact]
    public async Task InertSurfaceAnalyzer_TwoUnrelatedChains_ReportsBCF3029Twice()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Div["ok"];

                private void Warm()
                {
                    var first = Div.Class("a")[Span["one"]];
                    var second = Div.Class("b")[Span["two"]];
                }
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(source);

        Assert.Equal(2, diagnostics.Count(static d => d.Id == "BCF3029"));
    }

    /// <summary>
    /// Each of the two non-element child lists reports. Asserted as a count rather than through the theory
    /// set, which asks only that <em>something</em> was reported and would pass on either half alone.
    /// </summary>
    [Fact]
    public async Task InertSurfaceAnalyzer_ComponentAndSlotChildLists_EachReportBCF3029()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(
            BracketedComponentAndSlotOutsideABodySource);

        Assert.Equal(2, diagnostics.Count(static d => d.Id == "BCF3029"));
    }

    /// <summary>
    /// A lambda that returns an inert type is exempt wherever it is written, including outside a component
    /// altogether: what the design-time expression is read by is the caller's business, and this analyzer's
    /// condition is deliberately a return type rather than a position.
    /// </summary>
    [Fact]
    public async Task InertSurfaceAnalyzer_InertReturningLambdaInAnOrdinaryClass_DoesNotReportBCF3029()
    {
        const string source = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public static class Layouts
            {
                public static Func<View> Card() => () => Div.Class("card")[Span["hello"]];
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3029");
    }

    /// <summary>
    /// The surface written in a class that is not a component at all, in a method that returns something
    /// else. Nothing about this diagnostic asks whether the enclosing type is a component, and a helper
    /// class is where a chain most plausibly ends up by accident.
    /// </summary>
    [Fact]
    public async Task InertSurfaceAnalyzer_SurfaceInAnOrdinaryHelperMethod_ReportsBCF3029()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public static class Cards
            {
                public static string Render(string title)
                {
                    var v = Div.Class("card")[H2[title]];
                    return title;
                }
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(source);

        Assert.Single(diagnostics, static d => d.Id == "BCF3029");
    }

    /// <summary>
    /// A local function returning an inert type is exempt for the same reason a lambda is: it is the
    /// innermost enclosing declaration and it returns one. Roslyn analyses its body under the containing
    /// method's symbol, so the containing method here returns <c>void</c> deliberately — with an
    /// inert-returning one the local-function arm could be deleted and this would still pass, which is
    /// what a first draft of it did.
    /// </summary>
    [Fact]
    public async Task InertSurfaceAnalyzer_InertReturningLocalFunctionInAVoidMethod_DoesNotReportBCF3029()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Div["ok"];

                private void Warm()
                {
                    static View Card(string title) => Div.Class("card")[H2[title]];

                    Consume(Card("ok"));
                }

                private static void Consume(View view) { }
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3029");
    }

    /// <summary>
    /// Nothing is reported when the surface does not resolve. The compilation then has no way to tell a
    /// design-time expression from an ordinary method, and the cost of guessing is an Error on correct code.
    /// </summary>
    [Fact]
    public async Task InertSurfaceAnalyzer_WithoutTheRuntime_ReportsNothing()
    {
        const string source = """
            public static class Cards
            {
                public static string Render(string title) => title;
            }
            """;

        var compilation = CompilationTestHost.CreateCompilationWithoutRuntime(("Cards.cs", source));

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<InertSurfaceAnalyzer>(compilation);

        Assert.Empty(diagnostics);
    }
}

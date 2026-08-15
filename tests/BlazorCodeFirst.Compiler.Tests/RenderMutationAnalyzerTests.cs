using System.Collections.Immutable;
using System.Threading.Tasks;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Tests for <see cref="RenderMutationAnalyzer"/> (BCF3001).
/// Verifies that direct state mutations in the Body rendering path are diagnosed,
/// while mutations inside recognized deferred event handlers are not.
/// </summary>
public sealed class RenderMutationAnalyzerTests
{
    // -----------------------------------------------------------------------
    // Sources that should report BCF3001
    // -----------------------------------------------------------------------

    private const string IncrementInTextSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private int _count;
            protected override View Body => Span[$"{_count++}"];
        }
        """;

    private const string AssignmentInTextSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private int _count;
            protected override View Body => Span[$"{_count = 4}"];
        }
        """;

    private const string CompoundAssignmentInTextSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private int _count;
            protected override View Body => Span[$"{_count += 4}"];
        }
        """;

    private const string DecrementInTextSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private int _count;
            protected override View Body => Span[$"{_count--}"];
        }
        """;

    private const string PropertyAssignmentInTextSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Span[$"{Count = 4}"];
        }
        """;

    private const string PropertyIncrementInTextSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Span[$"{Count++}"];
        }
        """;

    /// <summary>
    /// Negative control for the Html.OnClick exemption: the mutation's nearest enclosing lambda is
    /// the Html.If content lambda, not an OnClick handler, so it must still report BCF3001.
    /// </summary>
    private const string IncrementInHtmlIfContentLambdaSource = """
        using BlazorCodeFirst;

        public partial class Counter : BodyComponentBase
        {
            private bool _flag = true;
            private int _count;
            protected override View Body => Html.If(_flag, () => Html.Span[(_count++).ToString()]);
        }
        """;

    /// <summary>
    /// The same negative control in the <c>delegate</c> spelling: reaching an anonymous method is not
    /// itself the exemption. The If content is evaluated while the frames are built, so the
    /// classification declines it however it is written.
    /// </summary>
    private const string IncrementInHtmlIfContentAnonymousMethodSource = """
        using BlazorCodeFirst;

        public partial class Counter : BodyComponentBase
        {
            private bool _flag = true;
            private int _count;
            protected override View Body =>
                Html.If(_flag, delegate { return Html.Span[(_count++).ToString()]; });
        }
        """;

    // -----------------------------------------------------------------------
    // Sources that must NOT report BCF3001
    // -----------------------------------------------------------------------

    /// <summary>
    /// Positive case for the Html.OnClick exemption (simple increment): the mutation's nearest
    /// enclosing lambda is the (reduced) sole argument of the Html-mirror
    /// <c>ElementView.OnClick(...)</c> extension call, so it must not report BCF3001.
    /// </summary>
    private const string IncrementInHtmlOnClickHandlerSource = """
        using BlazorCodeFirst;

        public partial class Counter : BodyComponentBase
        {
            private int _count;
            protected override View Body => Html.Button.OnClick(() => _count++)["Increment"];
        }
        """;

    /// <summary>Same exemption, but the mutation targets a property rather than a field.</summary>
    private const string PropertyIncrementInHtmlOnClickHandlerSource = """
        using BlazorCodeFirst;

        public partial class Counter : BodyComponentBase
        {
            private int Count { get; set; }
            protected override View Body => Html.Button.OnClick(() => Count++)["Increment"];
        }
        """;

    /// <summary>
    /// Same exemption, but the handler is written with the <c>delegate</c> keyword. Both spellings are
    /// the same argument to the same parameter, and the walk must reach the classification for either
    /// (#209).
    /// </summary>
    private const string IncrementInHtmlOnClickAnonymousMethodSource = """
        using BlazorCodeFirst;

        public partial class Counter : BodyComponentBase
        {
            private int _count;
            protected override View Body => Html.Button.OnClick(delegate { _count++; })["Increment"];
        }
        """;

    /// <summary>
    /// A reference capture assigns the captured value, and that is the only thing a capture is for: the
    /// action runs when the reference changes, which is after the frames are built. Same exemption as an
    /// event handler and a bind setter, on the channel that has no other spelling (#309).
    /// </summary>
    private const string CaptureInElementRefSource = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;

        public partial class Counter : BodyComponentBase
        {
            private ElementReference _input;
            protected override View Body => Html.Input.Ref(r => _input = r);
        }
        """;

    /// <summary>The same exemption on the component receiver, whose action takes the component itself.</summary>
    private const string CaptureInComponentRefSource = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;

        public class Row : ComponentBase { }

        public partial class Counter : BodyComponentBase
        {
            private Row? _row;
            protected override View Body => Html.Component<Row>().Ref(c => _row = c);
        }
        """;

    private const string HelperMutationSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class Counter : BodyComponentBase
        {
            private int _count;

            protected override View Body => Span[MutateAndReturnText()];

            private string MutateAndReturnText()
            {
                _count++;
                return _count.ToString();
            }
        }
        """;

    // -----------------------------------------------------------------------
    // Theory data
    // -----------------------------------------------------------------------

    /// <summary>Body-path mutations that must each be diagnosed as a BCF3001 error.</summary>
    public static TheoryData<string> MutationSourcesThatReportBCF3001 { get; } = BuildTheoryData(
        IncrementInTextSource,
        AssignmentInTextSource,
        CompoundAssignmentInTextSource,
        DecrementInTextSource,
        PropertyAssignmentInTextSource,
        PropertyIncrementInTextSource,
        IncrementInHtmlIfContentLambdaSource,
        IncrementInHtmlIfContentAnonymousMethodSource);

    /// <summary>Deferred mutations (event handlers, helper methods) that must not report BCF3001.</summary>
    public static TheoryData<string> MutationSourcesThatDoNotReportBCF3001 { get; } = BuildTheoryData(
        IncrementInHtmlOnClickHandlerSource,
        PropertyIncrementInHtmlOnClickHandlerSource,
        IncrementInHtmlOnClickAnonymousMethodSource,
        CaptureInElementRefSource,
        CaptureInComponentRefSource,
        HelperMutationSource);

    private static TheoryData<string> BuildTheoryData(params string[] sources)
    {
        var data = new TheoryData<string>();

        foreach (var source in sources)
        {
            data.Add(source);
        }

        return data;
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(MutationSourcesThatReportBCF3001))]
    public async Task RenderMutationAnalyzer_MutationInBodyRenderPath_ReportsBCF3001(string source)
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.Contains(diagnostics, static d => d.Id == "BCF3001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [MemberData(nameof(MutationSourcesThatDoNotReportBCF3001))]
    public async Task RenderMutationAnalyzer_DeferredMutation_DoesNotReportBCF3001(string source)
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3001");
    }

    [Fact]
    public async Task OnHandler_Mutation_IsExempt()
    {
        const string source = """
            using BlazorCodeFirst;
            public partial class C : BodyComponentBase
            {
                private int _n;
                protected override View Body => Html.Div.On("onmouseenter", () => _n++);
            }
            """;
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3001");
    }

    [Fact]
    public async Task AsyncOnClickHandler_NestedLambdaMutation_IsExempt()
    {
        // Regression: the nested lambda i => total += i is inside the deferred handler; must NOT fire BCF3001.
        const string source = """
            using System.Collections.Generic;
            using System.Linq;
            using BlazorCodeFirst;
            public partial class C : BodyComponentBase
            {
                private int total;
                private List<int> items = new();
                protected override View Body =>
                    Html.Button.OnClick(async () => { await System.Threading.Tasks.Task.Yield(); items.ForEach(i => total += i); })["Sum"];
            }
            """;
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3001");
    }

    [Fact]
    public async Task AttrValueMutation_IsReported()
    {
        // .Attr value runs during render, a mutation there is a real BCF3001 (not a deferred handler).
        const string source = """
            using BlazorCodeFirst;
            public partial class C : BodyComponentBase
            {
                private int _n;
                protected override View Body => Html.Div.Attr("data-n", (_n++).ToString());
            }
            """;
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);
        Assert.Contains(diagnostics, static d => d.Id == "BCF3001");
    }

    [Fact]
    public async Task RenderMutationAnalyzer_NamedHandlerArgument_DoesNotReportBCF3001()
    {
        // The handler is identified by the parameter it binds to, not by its position. A named argument
        // puts it first, which used to defeat the deferred-handler exemption and produce a false BCF3001.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private int _n;

                protected override View Body =>
                    Div.On(handler: () => _n++, eventName: "onclick");
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "BCF3001");
    }

    [Fact]
    public async Task RenderMutationAnalyzer_PositionalHandlerArgument_StillDoesNotReportBCF3001()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private int _n;

                protected override View Body => Div.On("onclick", () => _n++);
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "BCF3001");
    }

    [Fact]
    public async Task RenderMutationAnalyzer_MutationOutsideAnyHandler_StillReportsBCF3001()
    {
        // Guard against the fix widening the exemption: a mutation that is not inside a handler lambda
        // must still be reported.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private int _n;

                protected override View Body => Span[$"{_n++}"];
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.Single(diagnostics, d => d.Id == "BCF3001");
    }

    /// <summary>
    /// A block-bodied accessor is the design-time expression as much as an expression-bodied one. The
    /// condition is asked of the accessor symbol rather than of the declaration's syntax (#220), and
    /// <c>MethodKind.PropertyGet</c> answers the same for either spelling — where a walk to the enclosing
    /// <c>PropertyDeclarationSyntax</c> crossed a different set of nodes to get there.
    /// </summary>
    [Fact]
    public async Task RenderMutationAnalyzer_MutationInBlockBodiedGetter_ReportsBCF3001()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private int _n;

                protected override View Body
                {
                    get
                    {
                        _n++;
                        return Span[$"{_n}"];
                    }
                }
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.Single(diagnostics, d => d.Id == "BCF3001");
    }

    /// <summary>
    /// Being an <c>override</c> is not the condition; being the design-time expression is. A component may
    /// override anything its own bases declare, and a mutation in such an accessor runs when that member is
    /// read, which is not the render path this diagnostic fences off.
    /// </summary>
    [Fact]
    public async Task RenderMutationAnalyzer_MutationInAnUnrelatedOverriddenGetter_DoesNotReportBCF3001()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public abstract class Labelled : BodyComponentBase
            {
                protected abstract string Label { get; }
            }

            public partial class Counter : Labelled
            {
                private int _n;

                protected override string Label => (_n++).ToString();

                protected override View Body => Span[Label];
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "BCF3001");
    }

    [Fact]
    public async Task RenderMutationAnalyzer_SpoofedDecorationsNamespace_StillReportsBCF3001()
    {
        // A user-defined type sharing the name must not be able to claim the exemption. Since #194 that
        // follows from the exemption being keyed on the symbols resolved out of the referenced runtime,
        // rather than from the namespace walk this test was written against; it is kept because it is the
        // only spoof test that runs against a compilation where the real runtime IS referenced, which the
        // in-source surface tests below cannot be.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace Evil.BlazorCodeFirst
            {
                public static class Decorations
                {
                    public static View On(this View view, string eventName, System.Action handler) => view;
                }
            }

            public partial class Counter : BodyComponentBase
            {
                private int _n;

                protected override View Body =>
                    Evil.BlazorCodeFirst.Decorations.On(Div, "onclick", () => _n++);
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.Single(diagnostics, d => d.Id == "BCF3001");
    }

    // -----------------------------------------------------------------------
    // Receiver anchoring: a decoration is defined by the builder it extends, not by its name. These are
    // the only tests that can exercise that guard, because the shipped runtime declares every decoration
    // on ElementView and so can only prove the positive side — hence the in-source surface, which has
    // to be the only BlazorCodeFirst in scope. Compare MembersWithTheWrongDeclaredTypes_AreNotRecognized
    // in BracketSurfaceGeneratorTests.
    // -----------------------------------------------------------------------

    /// <summary>
    /// A <c>BlazorCodeFirst</c> surface declared in-source whose <c>Decorations</c> members carry the right
    /// names and delegate shapes on <paramref name="receiver"/>, written on <c>_receiver</c> of that same
    /// type. <paramref name="call"/> becomes the component's design-time expression and <c>_n</c> is the
    /// state it may mutate.
    /// </summary>
    /// <remarks>
    /// One template with the receiver as its only variable, so the two receivers differ in nothing else:
    /// what a test proves about <c>View</c> is otherwise as likely to come from the in-source surface
    /// itself as from the receiver being wrong.
    /// </remarks>
    private static ImmutableArray<Diagnostic> SurfaceDiagnostics(string receiver, string call)
    {
        var compilation = CompilationTestHost.CreateCompilationWithoutRuntime(("Surface.cs", $$"""
            namespace BlazorCodeFirst;

            public readonly struct View { }

            public readonly struct ElementView { }

            public abstract class BodyComponentBase
            {
                protected abstract View Body { get; }
            }

            // Declared and empty: KnownSymbols.TryCreate keys on BlazorCodeFirst.Html, and without it the
            // analyzer registers nothing, which would make the ElementView case below pass vacuously.
            public static class Html { }

            public static class Decorations
            {
                public static View OnClick(this {{receiver}} target, System.Action handler) => default;

                public static View On(this {{receiver}} target, string eventName, System.Action handler) =>
                    default;

                // An .On overload carrying a second delegate. Nothing the runtime declares has this shape;
                // it is here because "the argument whose type is a delegate" selects both of these, which
                // is the rule the deferred-handler exemption applied before it asked KnownSymbols (#221).
                public static View On(
                    this {{receiver}} target,
                    string eventName,
                    System.Action handler,
                    System.Action<int> completed) => default;

                public static View Bind<T>(
                    this {{receiver}} target,
                    string attribute,
                    string eventName,
                    System.Func<T> get,
                    System.Action<T> set) => default;
            }

            public partial class Counter : BodyComponentBase
            {
                private int _n;
                private {{receiver}} _receiver;

                protected override View Body => _receiver.{{call}};
            }
            """));

        return CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(compilation)
            .GetAwaiter().GetResult();
    }

    /// <summary>Every decoration call the surface above can be asked for, as written on <c>_receiver</c>.</summary>
    public static TheoryData<string> DecorationCallsWithAMutatingHandler { get; } = BuildTheoryData(
        "OnClick(() => _n++)",
        """On("onclick", () => _n++)""",
        """Bind("value", "oninput", () => _n, v => _n = v)""");

    [Theory]
    [MemberData(nameof(DecorationCallsWithAMutatingHandler))]
    public void HandlerOnADecorationExtendingElementView_DoesNotReportBcf3001(string call) =>
        Assert.DoesNotContain(SurfaceDiagnostics("ElementView", call), d => d.Id == "BCF3001");

    [Theory]
    [MemberData(nameof(DecorationCallsWithAMutatingHandler))]
    public void HandlerOnADecorationExtendingSomethingElse_StillReportsBcf3001(string call) =>
        // The name, the namespace and the delegate parameters match the real decoration exactly; only the
        // receiver differs, and a decoration is defined by the builder it extends. A name-and-namespace
        // test cannot tell the two apart, so it exempted the mutation and BCF3001 went quiet for a call
        // the compiler never recognized as a decoration in the first place.
        Assert.Contains(SurfaceDiagnostics("View", call), d => d.Id == "BCF3001");

    // -----------------------------------------------------------------------
    // Which argument of an event decoration the exemption belongs to. The runtime declares no overload
    // with a second delegate parameter, so the in-source surface is the only place this can be asked —
    // and KnownSymbolsSyncTests is what makes sure it stays that way, by going red if one is declared.
    // -----------------------------------------------------------------------

    /// <summary>
    /// A mutation in a second delegate argument is not exempt. Whether one written in an options or
    /// completion callback is deferred is undecided, and the delegate-shape test this replaces answered
    /// it "yes" for every delegate argument without anything having decided anything (#221).
    /// </summary>
    [Fact]
    public void MutationInASecondDelegateArgument_StillReportsBcf3001() =>
        Assert.Contains(
            SurfaceDiagnostics("ElementView", """On("onclick", () => { }, v => _n = v)"""),
            d => d.Id == "BCF3001");

    /// <summary>
    /// The same overload's real handler is reported too, which is the deliberate cost of
    /// <c>KnownSymbols.TryGetEventParameters</c> answering <see langword="false"/> for a shape it cannot
    /// read rather than guessing at one of two delegates.
    /// </summary>
    /// <remarks>
    /// Stated rather than left to be discovered, because a spurious BCF3001 on a correct handler is the
    /// wrong degradation — the same asymmetry the analyzer's own remarks name for a missing surface. What
    /// makes it acceptable here is that it cannot reach an author: this shape exists only in this fixture,
    /// and <c>KnownSymbolsSyncTests.EveryEventDecoration_ResolvesItsHandlerArgument</c> fails the moment
    /// the runtime declares one, naming the decision to be made instead of shipping either answer.
    /// </remarks>
    [Fact]
    public void MutationInTheHandlerOfAnUnreadableEventShape_AlsoReportsBcf3001() =>
        Assert.Contains(
            SurfaceDiagnostics("ElementView", """On("onclick", () => _n++, v => { })"""),
            d => d.Id == "BCF3001");

    // -----------------------------------------------------------------------
    // Bind setter exemption: the setter runs after the render, so it is a deferred handler like
    // OnClick/On, while the getter is evaluated while the frames are built and stays checked.
    // -----------------------------------------------------------------------

    /// <summary>Wraps <paramref name="body"/> as the members of a BodyComponentBase subclass, alongside
    /// a minimal two-way-bindable component so a component <c>.Bind</c> call resolves; mirrors the
    /// Probe fixture in ComponentBindGeneratorTests.</summary>
    private static ImmutableArray<Diagnostic> Diags(string body)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public sealed class Probe : Microsoft.AspNetCore.Components.ComponentBase
            {
                [Microsoft.AspNetCore.Components.Parameter] public string Value { get; set; } = "";
                [Microsoft.AspNetCore.Components.Parameter]
                public Microsoft.AspNetCore.Components.EventCallback<string> ValueChanged { get; set; }

                // A parameter of the component's own type, so TValue and TComponent coincide. That is
                // the shape under which the type-comparing derivation replaced by #206 read the
                // selector as the setter.
                [Microsoft.AspNetCore.Components.Parameter] public Probe? Self { get; set; }
                [Microsoft.AspNetCore.Components.Parameter]
                public Microsoft.AspNetCore.Components.EventCallback<Probe> SelfChanged { get; set; }
            }

            public partial class Counter : BodyComponentBase
            {
                {{body}}
            }
            """;

        return CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source).GetAwaiter().GetResult();
    }

    private static void AssertDiagnostic(string body, string id) =>
        Assert.Contains(Diags(body), d => d.Id == id);

    private static void AssertNoDiagnostics(string body) =>
        Assert.DoesNotContain(Diags(body), d => d.Severity == DiagnosticSeverity.Error);

    [Fact]
    public void Bind_ExplicitSetterLambda_DoesNotReportBcf3001()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _name, v => _name = v.Trim());
            """;

        AssertNoDiagnostics(body);
    }

    /// <summary>The setter above in the <c>delegate</c> spelling: the same argument bound to the same
    /// <c>Action&lt;T&gt;</c> parameter, and exempt for the same reason (#209).</summary>
    [Fact]
    public void Bind_ExplicitSetterAnonymousMethod_DoesNotReportBcf3001()
    {
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => _name, delegate(string v) { _name = v; });
            """;

        AssertNoDiagnostics(body);
    }

    [Fact]
    public void Bind_ComponentExplicitSetterLambda_DoesNotReportBcf3001()
    {
        // Probe declares Value and ValueChanged; see ComponentBindGeneratorTests.
        const string body = """
            private string _name = "";
            protected override View Body =>
                Html.Component<Probe>().Bind(c => c.Value, () => _name, v => _name = v);
            """;

        AssertNoDiagnostics(body);
    }

    /// <summary>
    /// The selector is not a deferred position. It is evaluated for the parameter it names while the
    /// frames are built, exactly as the getter is, so a mutation written in it is BCF3001 — which is
    /// what ARCHITECTURE.md 付録A's BCF3001 row says by naming only the setter argument. This shape,
    /// a bound parameter whose type is the component's own, is where the derivation #206 replaced
    /// answered otherwise.
    /// </summary>
    /// <remarks>
    /// The selector here is a block body that mutates state before returning <c>c.Self</c>, not
    /// <c>p =&gt; p.Property</c>, so the generator's own selector check rejects it and reports BCF3005
    /// independently (<c>TryGetSelectorProperty</c>,
    /// src/BlazorCodeFirst.Compiler/Analysis/RenderExpressionAnalyzer.cs:397-401). The fixture is
    /// therefore already an error on the generator path; BCF3001 only adds a second diagnostic to a
    /// compile that was already wrong, and never rescues one that would otherwise compile silently.
    /// BCF3005 comes from the generator and does not suppress this analyzer, which is why both fire
    /// and the assertion above is real.
    /// </remarks>
    [Fact]
    public void Bind_ComponentSelectorMutatingState_ReportsBcf3001()
    {
        const string body = """
            private Probe? _p;
            private bool _touched;
            protected override View Body =>
                Html.Component<Probe>().Bind(c => { _touched = true; return c.Self; }, () => _p, v => _p = v);
            """;

        AssertDiagnostic(body, "BCF3001");
    }

    [Fact]
    public void Bind_GetterLambdaMutatingState_ReportsBcf3001()
    {
        // The getter is evaluated while the frames are built, so a mutation there is still a
        // one-way-flow break and must keep reporting BCF3001. Only the setter is exempt.
        //
        // This has to supply an explicit setter. In the getter-only form the getter must be
        // assignable (BCF3018), and an assignable expression cannot carry a mutation, so BCF3018
        // would fire first and BCF3001 would never be reached. Supplying an explicit setter lifts
        // the assignability requirement from the getter, which is the only shape where a mutating
        // getter is otherwise legal.
        const string body = """
            private string _name = "";
            private int _reads;
            protected override View Body =>
                Html.Input.Bind("value", "oninput", () => (_reads++).ToString(), v => _name = v);
            """;

        AssertDiagnostic(body, "BCF3001");
    }
}

using System.Collections.Immutable;
using System.Threading.Tasks;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Tests for <see cref="RenderMutationAnalyzer"/> (BCF3001).
/// Verifies that direct state mutations in the Body rendering path are diagnosed,
/// while mutations inside recognized deferred event handler lambdas are not.
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

    // -----------------------------------------------------------------------
    // Sources that must NOT report BCF3001
    // -----------------------------------------------------------------------

    /// <summary>
    /// Positive case for the Html.OnClick exemption (simple increment): the mutation's nearest
    /// enclosing lambda is the (reduced) sole argument of the Html-mirror
    /// <c>ElementBuilder.OnClick(...)</c> extension call, so it must not report BCF3001.
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
        IncrementInHtmlIfContentLambdaSource);

    /// <summary>Deferred mutations (event handlers, helper methods) that must not report BCF3001.</summary>
    public static TheoryData<string> MutationSourcesThatDoNotReportBCF3001 { get; } = BuildTheoryData(
        IncrementInHtmlOnClickHandlerSource,
        PropertyIncrementInHtmlOnClickHandlerSource,
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
    // on ElementBuilder and so can only prove the positive side — hence the in-source surface, which has
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

            public readonly struct ElementBuilder { }

            public abstract class BodyComponentBase
            {
                protected abstract View Body { get; }
            }

            // Declared and empty: KnownSymbols.TryCreate keys on BlazorCodeFirst.Html, and without it the
            // analyzer registers nothing, which would make the ElementBuilder case below pass vacuously.
            public static class Html { }

            public static class Decorations
            {
                public static View OnClick(this {{receiver}} target, System.Action handler) => default;

                public static View On(this {{receiver}} target, string eventName, System.Action handler) =>
                    default;

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
    public void HandlerOnADecorationExtendingElementBuilder_DoesNotReportBcf3001(string call) =>
        Assert.DoesNotContain(SurfaceDiagnostics("ElementBuilder", call), d => d.Id == "BCF3001");

    [Theory]
    [MemberData(nameof(DecorationCallsWithAMutatingHandler))]
    public void HandlerOnADecorationExtendingSomethingElse_StillReportsBcf3001(string call) =>
        // The name, the namespace and the delegate parameters match the real decoration exactly; only the
        // receiver differs, and a decoration is defined by the builder it extends. A name-and-namespace
        // test cannot tell the two apart, so it exempted the mutation and BCF3001 went quiet for a call
        // the compiler never recognized as a decoration in the first place.
        Assert.Contains(SurfaceDiagnostics("View", call), d => d.Id == "BCF3001");

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

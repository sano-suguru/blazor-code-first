namespace BlazorCodeFirst.Compiler.Tests;

public sealed class OpaqueCallDiagnosticTests
{
    private const string InertHelperSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div[Card("hello")];

            private static View Card(string title) => Html.Span[title];
        }
        """;

    private const string ViewPartSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div[Card("hello")];

            [ViewPart]
            private static View Card(string title) => Html.Span[title];
        }
        """;

    [Fact]
    public void ViewReturningCall_WhenCalleeBuildsFromTheDesignTimeSurface_ReportsBCF3030()
    {
        var result = CompilationTestHost.RunGenerator(InertHelperSource);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");
    }

    [Fact]
    public void ViewReturningCall_WhenCalleeBuildsFromTheDesignTimeSurface_ReportsNoBCF1003()
    {
        // BCF3030 names the fix. BCF1003 would only say the body could not be translated, and
        // ComponentModelFactory.Expand suppresses it once an actionable error is present.
        var result = CompilationTestHost.RunGenerator(InertHelperSource);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void ViewReturningCall_WhenCalleeIsAViewPart_ReportsNothing()
    {
        var result = CompilationTestHost.RunGenerator(ViewPartSource);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF2001");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    private const string FragmentBackedSource = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div[Wrap()];

            private static View Wrap()
            {
                RenderFragment fragment = builder => builder.AddContent(0, "x");
                return fragment;
            }
        }
        """;

    [Fact]
    public void ViewReturningCall_WhenCalleeDoesNotUseTheSurface_ReportsBCF2001()
    {
        var result = CompilationTestHost.RunGenerator(FragmentBackedSource);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF2001");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, diagnostic.Severity);
    }

    [Fact]
    public void ViewReturningCall_WhenCalleeDoesNotUseTheSurface_EmitsAddContentThroughViewRuntime()
    {
        var result = CompilationTestHost.RunGenerator(FragmentBackedSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // The call itself is fully qualified by ExpressionTemplateFactory, because the generated file
        // carries no using directives.
        Assert.Contains(
            "global::BlazorCodeFirst.CompilerServices.ViewRuntime.FragmentOf(global::C.Wrap())",
            generated);

        // Blazor opens a region for the fragment itself, exactly as it does for
        // RenderFragmentContentNode, so the emission writes no OpenRegion of its own.
        Assert.DoesNotContain("OpenRegion", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenRootIsAnOpaqueCall_ReportsBCF3003()
    {
        const string Source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using System.Collections.Generic;

            public partial class C : BodyComponentBase
            {
                private readonly List<string> _items = new() { "a" };

                protected override View Body =>
                    Html.ForEach(_items, x => x, x => Wrap(x));

                private static View Wrap(string text)
                {
                    RenderFragment fragment = builder => builder.AddContent(0, text);
                    return fragment;
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(Source);

        // A fragment opens no keyable frame, so SetKey has nothing to attach to. Same rule that already
        // rejects Fragment and Raw as content roots.
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }
}

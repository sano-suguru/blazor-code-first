namespace BlazorCodeFirst.Compiler.Tests;

public sealed class OpaqueCallDiagnosticTests
{
    /// <summary>
    /// A component calling a helper whose declaration carries <c>$ATTRIBUTE$</c>. The helper builds its
    /// <c>View</c> from the design-time surface, so without the attribute the call renders nothing.
    /// </summary>
    private const string SurfaceBuiltHelper = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div[Card("hello")];

            $ATTRIBUTE$
            private static View Card(string title) => Html.Span[title];
        }
        """;

    /// <summary>A helper whose <c>View</c> comes from a real <c>RenderFragment</c>: the genuine Opaque case.</summary>
    private const string FragmentBackedHelper = """
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
    public void ViewReturningCall_WhenCalleeBuildsFromTheDesignTimeSurface_ReportsBCF3030AndNotBCF1003()
    {
        var result = CompilationTestHost.RunGenerator(
            SurfaceBuiltHelper.Replace("$ATTRIBUTE$", string.Empty));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");

        // BCF3030 names the fix. BCF1003 would only say the body could not be translated, and
        // ComponentModelFactory.Expand suppresses it once an actionable error is present.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void ViewReturningCall_WhenCalleeIsAViewPart_ReportsNothing()
    {
        var result = CompilationTestHost.RunGenerator(
            SurfaceBuiltHelper.Replace("$ATTRIBUTE$", "[ViewPart]"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF2001");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewReturningCall_WhenCalleeDoesNotUseTheSurface_ReportsBCF2001AndRendersTheFragment()
    {
        var result = CompilationTestHost.RunGenerator(FragmentBackedHelper);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF2001");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, diagnostic.Severity);

        // The call itself is fully qualified by ExpressionTemplateFactory, because the generated file
        // carries no using directives.
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains(
            "global::BlazorCodeFirst.CompilerServices.ViewRuntime.FragmentOf(global::C.Wrap())",
            generated);

        // Blazor opens a region for the fragment itself, exactly as it does for RenderFragmentContentNode,
        // so the emission writes no OpenRegion of its own.
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

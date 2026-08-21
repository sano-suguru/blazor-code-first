using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Tests;

public class CssScopePropagationTests
{
    [Fact]
    public void HostElement_WithMatchingCssScopeFile_CarriesTheScope()
    {
        const string source = """
            using BlazorCodeFirst;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Html.Div.OnClick(() => { });
            }
            """;

        var model = ModelSingleScopedComponent(
            [("Counter.cs", source)],
            [("Counter.cs.css", "bcf-abcd1234")]);

        var div = Assert.IsType<ElementNode>(model.RootNode);
        Assert.Equal("bcf-abcd1234", div.CssScope);
    }

    private const string CounterSource = """
        using BlazorCodeFirst;

        public partial class Counter : BodyComponentBase
        {
            protected override View Body => Html.Div.OnClick(() => { });
        }
        """;

    [Fact]
    public void HostElement_WithCssScopeFilePathUsingDifferentSeparatorsThanTheSourceFile_StillCarriesTheScope()
    {
        var model = ModelSingleScopedComponent(
            [("App/Counter.cs", CounterSource)],
            [(@"App\Counter.cs.css", "bcf-abcd1234")]);

        var div = Assert.IsType<ElementNode>(model.RootNode);
        Assert.Equal("bcf-abcd1234", div.CssScope);
    }

    [Fact]
    public void MatchingCssScopeFile_WithDifferentPathSeparatorsThanTheSourceFile_IsNotReportedAsOrphaned()
    {
        var result = CompilationTestHost.RunGeneratorWithCssScopes(
            [("App/Counter.cs", CounterSource)],
            [(@"App\Counter.cs.css", "bcf-abcd1234")]);

        CompilationTestHost.AssertNoDiagnostics(result);
    }

    [Fact]
    public void HostElement_WithNoMatchingCssScopeFile_CarriesNoScope()
    {
        const string source = """
            using BlazorCodeFirst;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Html.Div.OnClick(() => { });
            }
            """;

        var model = ModelSingleScopedComponent([("Counter.cs", source)], []);

        var div = Assert.IsType<ElementNode>(model.RootNode);
        Assert.Null(div.CssScope);
    }

    [Fact]
    public void ViewPartExpandedElement_CarriesTheViewPartsOwnFileScope_NotTheHosts()
    {
        const string hostSource = """
            using BlazorCodeFirst;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Widgets.Badge();
            }
            """;
        const string viewPartSource = """
            using BlazorCodeFirst;

            public static class Widgets
            {
                [ViewPart]
                public static View Badge() => Html.Span.OnClick(() => { });
            }
            """;

        var model = ModelSingleScopedComponent(
            [("Counter.cs", hostSource), ("Widgets.cs", viewPartSource)],
            [("Counter.cs.css", "bcf-host"), ("Widgets.cs.css", "bcf-viewpart")]);

        var expansion = Assert.IsType<ExpansionNode>(model.RootNode);
        var span = Assert.IsType<ElementNode>(expansion.Body);
        Assert.Equal("bcf-viewpart", span.CssScope);
    }

    [Fact]
    public void ContentPassedIntoAViewPartSlot_KeepsTheCallersScope_NotTheViewParts()
    {
        const string hostSource = """
            using BlazorCodeFirst;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body =>
                    Widgets.Card()[Html.Span.OnClick(() => { })];
            }
            """;
        const string viewPartSource = """
            using BlazorCodeFirst;

            public static class Widgets
            {
                [ViewPart]
                public static SlotView Card() => Html.Div[Html.Slot];
            }
            """;

        var model = ModelSingleScopedComponent(
            [("Counter.cs", hostSource), ("Widgets.cs", viewPartSource)],
            [("Counter.cs.css", "bcf-host"), ("Widgets.cs.css", "bcf-viewpart")]);

        var expansion = Assert.IsType<ExpansionNode>(model.RootNode);
        var div = Assert.IsType<ElementNode>(expansion.Body);
        Assert.Equal("bcf-viewpart", div.CssScope);

        var span = Assert.IsType<ElementNode>(Assert.Single(div.Children.AsImmutableArray()));
        Assert.Equal("bcf-host", span.CssScope);
    }

    private static ComponentModel ModelSingleScopedComponent(
        (string Path, string Source)[] sources, (string CssPath, string Scope)[] cssScopes)
    {
        var result = CompilationTestHost.RunGeneratorWithCssScopes(sources, cssScopes);
        Assert.True(result.TrackedSteps.ContainsKey("ComponentModeling"),
            "Expected tracked step 'ComponentModeling' but found: " +
            string.Join(", ", result.TrackedSteps.Keys));

        var models = result.TrackedSteps["ComponentModeling"]
            .SelectMany(static step => step.Outputs)
            .Select(static output => ((ComponentModelResult)output.Value).Model)
            .Where(static model => model is not null)
            .ToImmutableArray();

        return Assert.Single(models)!;
    }
}

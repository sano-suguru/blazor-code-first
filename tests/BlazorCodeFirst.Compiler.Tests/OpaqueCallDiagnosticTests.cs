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
}

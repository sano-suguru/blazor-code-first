namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlElementGeneratorTests
{
    private const string DivTextSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div["hello"];
        }
        """;

    private const string MixedContentSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private int _n = 0;
            protected override View Body =>
                Html.Div["Count: ", Html.Span["x"], "tail"];
        }
        """;

    [Fact]
    public void Div_WithBareText_EmitsDivWithAddContentNoSpan()
    {
        var result = CompilationTestHost.RunGenerator(DivTextSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddContent(1, \"hello\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Div_WithMixedContent_EmitsTextAndElementChildrenInPreorder()
    {
        var result = CompilationTestHost.RunGenerator(MixedContentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> text "Count: "(1) -> span(2) -> "x"(3) -> text "tail"(4)
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddContent(1, \"Count: \")", generated);
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, \"x\")", generated);
        Assert.Contains("__builder.AddContent(4, \"tail\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

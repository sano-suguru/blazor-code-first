namespace BlazorCompose.Compiler.Tests;

public sealed class HtmlElementTagGeneratorTests
{
    private const string ConstantTagSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            protected override View Body => Html.Element("nav", Html.Span("x"));
        }
        """;

    private const string NonConstantTagSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            private readonly string _tag = "nav";
            protected override View Body => Html.Element(_tag, Html.Span("x"));
        }
        """;

    private const string EmptyTagSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            protected override View Body => Html.Element("", Html.Span("x"));
        }
        """;

    [Fact]
    public void Element_WithConstantTag_EmitsOpenElementWithTagAndChild()
    {
        var result = CompilationTestHost.RunGenerator(ConstantTagSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"nav\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, \"x\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Element_WithNonConstantTag_ReportsBC3009()
    {
        var result = CompilationTestHost.RunGenerator(NonConstantTagSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BC3009");
    }

    [Fact]
    public void Element_WithEmptyTag_ReportsBC3009()
    {
        var result = CompilationTestHost.RunGenerator(EmptyTagSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BC3009");
    }
}

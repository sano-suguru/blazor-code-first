namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlElementTagGeneratorTests
{
    private const string ConstantTagSource = """
        using BlazorCodeFirst;

        public partial class C : ComposeComponentBase
        {
            protected override View Body => Html.Element("nav")[Html.Span["x"]];
        }
        """;

    private const string NonConstantTagSource = """
        using BlazorCodeFirst;

        public partial class C : ComposeComponentBase
        {
            private readonly string _tag = "nav";
            protected override View Body => Html.Element(_tag)[Html.Span["x"]];
        }
        """;

    private const string EmptyTagSource = """
        using BlazorCodeFirst;

        public partial class C : ComposeComponentBase
        {
            protected override View Body => Html.Element("")[Html.Span["x"]];
        }
        """;

    private const string WhitespaceTagSource = """
        using BlazorCodeFirst;

        public partial class C : ComposeComponentBase
        {
            protected override View Body => Html.Element("   ")[Html.Span["x"]];
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
    public void Element_WithNonConstantTag_ReportsBCF3009()
    {
        var result = CompilationTestHost.RunGenerator(NonConstantTagSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3009");
    }

    [Fact]
    public void Element_WithEmptyTag_ReportsBCF3009()
    {
        var result = CompilationTestHost.RunGenerator(EmptyTagSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3009");
    }

    // Pins the guard's IsNullOrWhiteSpace choice: a whitespace-only tag (not caught by a
    // narrower IsNullOrEmpty) must still be rejected, so it cannot lower to OpenElement(seq, "   ").
    [Fact]
    public void Element_WithWhitespaceTag_ReportsBCF3009()
    {
        var result = CompilationTestHost.RunGenerator(WhitespaceTagSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3009");
    }

    [Theory]
    [InlineData("Nav", "nav")]
    [InlineData("Header", "header")]
    [InlineData("Main", "main")]
    [InlineData("Article", "article")]
    [InlineData("H1", "h1")]
    [InlineData("Ul", "ul")]
    [InlineData("A", "a")]
    [InlineData("Img", "img")]
    public void CuratedHelper_EmitsItsTag(string helper, string tag)
    {
        var source = $$"""
            using BlazorCodeFirst;
            public partial class C : ComposeComponentBase
            {
                protected override View Body => Html.{{helper}};
            }
            """;
        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains($"__builder.OpenElement(0, \"{tag}\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlElementTagGeneratorTests
{
    // Non-constant child text, so the static fold leaves the element frames alone: what this test checks is
    // that Element(tag) resolves its tag onto an OpenElement call.
    private const string ConstantTagSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _x => "x";
            protected override View Body => Html.Element("nav")[Html.Span[_x]];
        }
        """;

    private const string NonConstantTagSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private readonly string _tag = "nav";
            protected override View Body => Html.Element(_tag)[Html.Span["x"]];
        }
        """;

    private const string EmptyTagSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Element("")[Html.Span["x"]];
        }
        """;

    private const string WhitespaceTagSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
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
        Assert.Contains("__builder.AddContent(2, _x)", generated);
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

    /// <summary>
    /// A tag no element can be named, written where a tag goes. Each spelling reaches the two render
    /// paths differently and none of them reaches what was written: a space makes the prerendered markup
    /// an element with a boolean attribute and the interactive path a torn-down circuit (#394), and a
    /// quote or a backslash survives into a literal position in generated source (#388). The check is on
    /// the characters, so all of them are one diagnostic.
    /// </summary>
    [Theory]
    [InlineData("\"a b\"")]
    [InlineData("\"a\\\"b\"")]
    [InlineData("@\"foo\\bar\"")]
    [InlineData("\"<div>\"")]
    [InlineData("\"div/\"")]
    [InlineData("\"_x\"")]
    [InlineData("\"1up\"")]
    public void Element_WithATagNoNameCanCarry_ReportsBCF3009(string tagArgument)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private string _x => "x";
                protected override View Body => Html.Element({{tagArgument}})[Html.Span[_x]];
            }
            """;
        var result = CompilationTestHost.RunGenerator(source);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3009");
    }

    /// <summary>
    /// The names outside the curated table that the check must keep admitting: an uppercase spelling,
    /// which is emitted as written and deliberately not curated; a custom element; an SVG tag, which is
    /// camelCase; and a curated tag reached through <c>Element</c> rather than its helper. Without these
    /// the widened check would pass by rejecting everything.
    /// </summary>
    [Theory]
    [InlineData("DIV")]
    [InlineData("my-widget")]
    [InlineData("linearGradient")]
    [InlineData("h1")]
    public void Element_WithATagAValidNameCanCarry_EmitsOpenElement(string tag)
    {
        var source = $$"""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private string _x => "x";
                protected override View Body => Html.Element("{{tag}}")[Html.Span[_x]];
            }
            """;
        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3009");
        Assert.Contains($"__builder.OpenElement(0, \"{tag}\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
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
            public partial class C : BodyComponentBase
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

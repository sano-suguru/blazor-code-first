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

    /// <summary>
    /// A body whose only variable is the <c>Element</c> tag argument, written as source text so a case
    /// can supply a verbatim string. The child is non-constant, which keeps the static fold away from
    /// the element and leaves the <c>OpenElement</c> call to assert on.
    /// </summary>
    private static string TagSource(string tagArgument) => $$"""
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _x => "x";
            protected override View Body => Html.Element({{tagArgument}})[Html.Span[_x]];
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

    /// <summary>
    /// A tag no element can be named, written where a tag goes. The empty and whitespace-only spellings
    /// are the ones the check has always rejected; the rest reach the two render paths differently and
    /// none of them reaches what was written. A space makes the prerendered markup an element with a
    /// boolean attribute and the interactive path a torn-down circuit (#394), and a quote or a backslash
    /// survives into a literal position in generated source (#388). The check is on the characters, so
    /// all of them are one diagnostic.
    /// </summary>
    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("\"a b\"")]
    [InlineData("\"a\\\"b\"")]
    [InlineData("@\"foo\\bar\"")]
    [InlineData("\"<div>\"")]
    [InlineData("\"div/\"")]
    [InlineData("\"_x\"")]
    [InlineData("\"1up\"")]
    public void Element_WithATagNoNameCanCarry_ReportsBCF3009(string tagArgument)
    {
        var result = CompilationTestHost.RunGenerator(TagSource(tagArgument));
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
        var result = CompilationTestHost.RunGenerator(TagSource($"\"{tag}\""));
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

    [Fact]
    public void ElementTagAlias_PlainStaticProperty_ResolvesToItsTag()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                private string _x => "x";

                static ElementView MyCard => Element("my-card");

                protected override View Body => MyCard[Span[_x]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3009");
        Assert.Contains("__builder.OpenElement(0, \"my-card\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ElementTagAlias_ComposesWithAttrChainAtTheCallSite()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                static ElementView MyCard => Element("my-card");

                protected override View Body => MyCard.Attr("variant", "wide")["x"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        CompilationTestHost.AssertNoDiagnostics(result);
        // Every operand here is constant, so this folds into markup (#140) the same way
        // Div.Class("card")["x"] would -- proof the alias composes into the existing fold, not just
        // the OpenElement path.
        Assert.Contains("__builder.AddMarkupContent(0, \"<my-card variant=\\\"wide\\\">x</my-card>\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ElementTagAlias_WithNonElementBody_FallsThroughWithNoNewDiagnostic()
    {
        // Body is a curated helper reference, not a bare Element("literal") call: out of the tag-only
        // scope this feature accepts (spec, "対象外"). Must not silently start resolving as an alias.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                static ElementView NotAnAlias => Div;

                protected override View Body => NotAnAlias["x"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1006");
        // Whatever this produced before this feature existed (BCF1003, most likely) is unaffected --
        // the point of this test is the *absence* of a new diagnostic, not a specific old one, so no
        // generated-source assertion here.
    }
}

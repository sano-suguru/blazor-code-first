using BlazorCodeFirst.Site.DocGen;
using Markdig;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class AstRewriterTests
{
    private static readonly HashSet<string> Known =
        new(["getting-started", "control-flow"], StringComparer.Ordinal);

    private static string Rewrite(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        AstRewriter.RewriteRelativeLinks(document, Known, "sample.md");

        using var writer = new StringWriter();
        document.ToHtml(writer, pipeline);
        return writer.ToString();
    }

    [Theory]
    [InlineData("[x](control-flow.md)", "href=\"/docs/control-flow\"")]
    [InlineData("[x](./control-flow.md)", "href=\"/docs/control-flow\"")]
    [InlineData("[x](./control-flow.md#loops)", "href=\"/docs/control-flow#loops\"")]
    [InlineData("[x](control-flow.md#step:1)", "href=\"/docs/control-flow#step:1\"")]
    [InlineData("[x](./control-flow.md#step:1)", "href=\"/docs/control-flow#step:1\"")]
    [InlineData("[x](control-flow.md#a/b)", "href=\"/docs/control-flow#a/b\"")]
    public void RewriteRelativeLinks_SiblingDocument_BecomesSpaRoute(string markdown, string expected) =>
        Assert.Contains(expected, Rewrite(markdown));

    [Theory]
    [InlineData("[x](#installation)", "href=\"#installation\"")]
    [InlineData("[x](#step:1)", "href=\"#step:1\"")]
    [InlineData("[x](/counter)", "href=\"/counter\"")]
    [InlineData("[x](https://example.com/a.md)", "href=\"https://example.com/a.md\"")]
    [InlineData("[x](mailto:a@example.com)", "href=\"mailto:a@example.com\"")]
    [InlineData("[x](mailto:a@example.com#note)", "href=\"mailto:a@example.com#note\"")]
    [InlineData("[x](mailto:notes.md)", "href=\"mailto:notes.md\"")]
    [InlineData("[x](tel:123)", "href=\"tel:123\"")]
    [InlineData("[x](../parent.md)", "href=\"../parent.md\"")]
    [InlineData("[x](sub/other.md)", "href=\"sub/other.md\"")]
    public void RewriteRelativeLinks_NonSiblingTargets_AreUntouched(string markdown, string expected) =>
        Assert.Contains(expected, Rewrite(markdown));

    [Fact]
    public void RewriteRelativeLinks_Image_IsUntouched() =>
        Assert.Contains("src=\"diagram.md\"", Rewrite("![alt](diagram.md)"));

    [Fact]
    public void RewriteRelativeLinks_BrokenLink_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Rewrite("[x](nope.md)"));
        Assert.Contains("sample.md", ex.Message);
        Assert.Contains("nope.md", ex.Message);
    }

    [Fact]
    public void RewriteRelativeLinks_BrokenLinkWithFragment_Throws()
    {
        // Before the fragment-split fix, a colon inside a fragment (e.g. "#a:b") made this shape
        // misread as a scheme and skip validation entirely, shipping a raw, un-rewritten ".md"
        // href instead of failing the build.
        var ex = Assert.Throws<InvalidOperationException>(() => Rewrite("[x](nope.md#a:b)"));
        Assert.Contains("sample.md", ex.Message);
        Assert.Contains("nope.md", ex.Message);
    }

    [Fact]
    public void RewriteRelativeLinks_QueryStringOnSibling_Throws()
    {
        // A query string on a document link is not a supported shape; failing loudly beats shipping
        // an un-rewritten ".md?x=1" href that 404s in the browser.
        var ex = Assert.Throws<InvalidOperationException>(() => Rewrite("[x](control-flow.md?x=1)"));
        Assert.Contains("sample.md", ex.Message);
    }

    [Fact]
    public void RewriteRelativeLinks_UppercaseExtension_Throws()
    {
        // Slugs are lowercase by rule (DocSlug), so "CONTROL-FLOW.MD" can never resolve; report it as
        // the casing mistake it is rather than as a missing document.
        var ex = Assert.Throws<InvalidOperationException>(() => Rewrite("[x](CONTROL-FLOW.MD)"));
        Assert.Contains("lowercase", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string AddHeadingLinks(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
            .Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        AstRewriter.AddHeadingLinks(document);

        using var writer = new StringWriter();
        document.ToHtml(writer, pipeline);
        return writer.ToString();
    }

    [Fact]
    public void AddHeadingLinks_H2_GetsAnchorToItsOwnSlug()
    {
        string html = AddHeadingLinks("## Getting Started\n");

        Assert.Contains("id=\"getting-started\"", html);
        Assert.Contains("href=\"#getting-started\"", html);
        Assert.Contains("class=\"headlink\"", html);
    }

    [Fact]
    public void AddHeadingLinks_AnchorHasDiscernibleText()
    {
        // An empty <a> has no accessible name, so the anchor carries real link text plus an
        // aria-label rather than relying on a CSS ::before glyph.
        string html = AddHeadingLinks("## Section\n");

        Assert.Contains(">#</a>", html);
        Assert.Contains("aria-label=", html);
    }

    [Theory]
    [InlineData("### Three\n", "#three")]
    [InlineData("#### Four\n", "#four")]
    [InlineData("##### Five\n", "#five")]
    [InlineData("###### Six\n", "#six")]
    public void AddHeadingLinks_H3ThroughH6_GetAnchors(string markdown, string expectedHref) =>
        Assert.Contains($"href=\"{expectedHref}\"", AddHeadingLinks(markdown));

    [Fact]
    public void AddHeadingLinks_H1_GetsNoAnchor()
    {
        // Bodies must not contain an h1 (MarkdownBodyRules enforces it), but the rewriter must not
        // add anchors to one if it ever sees it.
        Assert.DoesNotContain("headlink", AddHeadingLinks("# Top\n"));
    }

    [Fact]
    public void AddHeadingLinks_AddsOneAnchorPerHeading()
    {
        string html = AddHeadingLinks("## A\n\n## B\n");

        Assert.Equal(2, html.Split("class=\"headlink\"").Length - 1);
    }
}

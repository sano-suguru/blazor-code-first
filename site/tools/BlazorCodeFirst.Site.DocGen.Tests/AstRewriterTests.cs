using Markdig;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class AstRewriterTests
{
    /// <summary>Each document a link may resolve to, with the ids it publishes.</summary>
    /// <remarks>
    /// Two of the ids are shapes no heading text would produce. "step:1" and "a/b" are here because
    /// the rows below are about how a URL is split into path and fragment, and the split has to hold
    /// for a fragment that reads like a scheme or like a path.
    /// </remarks>
    private static readonly Dictionary<string, IReadOnlySet<string>> Known = new(StringComparer.Ordinal)
    {
        ["getting-started"] = new HashSet<string>(["installation"], StringComparer.Ordinal),
        ["control-flow"] = new HashSet<string>(["loops", "step:1", "a/b"], StringComparer.Ordinal),
    };

    private static string Rewrite(string markdown)
    {
        // Auto identifiers, because a link to a section of this same document is checked against the
        // ids this document publishes, and those are assigned at parse time.
        var pipeline = new MarkdownPipelineBuilder()
            .UseAutoIdentifiers(Markdig.Extensions.AutoIdentifiers.AutoIdentifierOptions.GitHub)
            .Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        AstRewriter.RewriteRelativeLinks(document, Known, "sample.md", "/docs");

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
    [InlineData("## Installation\n\n[x](#installation)", "href=\"#installation\"")]
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

    [Fact]
    public void RewriteRelativeLinks_SiblingDocumentWithNoSuchSection_Throws()
    {
        // The defect #405 records: the document resolves, so the link was accepted and the reader
        // landed at the top of a page with no such section -- which looks the same as landing where
        // the link meant to.
        var ex = Assert.Throws<InvalidOperationException>(() => Rewrite("[x](./control-flow.md#gone)"));

        Assert.Contains("sample.md", ex.Message, StringComparison.Ordinal);
        Assert.Contains("control-flow.md", ex.Message, StringComparison.Ordinal);
        Assert.Contains("gone", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RewriteRelativeLinks_SectionOfThisDocumentThatDoesNotExist_Throws()
    {
        // A link with no path is checked against this document's own headings. It is the same broken
        // link as one across documents, and the anchors are already in hand.
        var ex = Assert.Throws<InvalidOperationException>(() => Rewrite("## Installation\n\n[x](#instalation)"));

        Assert.Contains("sample.md", ex.Message, StringComparison.Ordinal);
        Assert.Contains("instalation", ex.Message, StringComparison.Ordinal);
    }

    // The non-ASCII half of the same check lives in MarkdownConverterTests, whose Japanese literals
    // site.yml already exempts from its English-only scan.

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

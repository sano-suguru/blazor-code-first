using BlazorCompose.Site.DocGen;
using Markdig;
using Xunit;

namespace BlazorCompose.Site.DocGen.Tests;

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
    public void RewriteRelativeLinks_SiblingDocument_BecomesSpaRoute(string markdown, string expected) =>
        Assert.Contains(expected, Rewrite(markdown));

    [Theory]
    [InlineData("[x](#installation)", "href=\"#installation\"")]
    [InlineData("[x](/counter)", "href=\"/counter\"")]
    [InlineData("[x](https://example.com/a.md)", "href=\"https://example.com/a.md\"")]
    [InlineData("[x](mailto:a@example.com)", "href=\"mailto:a@example.com\"")]
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
}

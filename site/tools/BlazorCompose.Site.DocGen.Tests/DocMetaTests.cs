using BlazorCompose.Site.DocGen;
using Markdig;
using Xunit;

namespace BlazorCompose.Site.DocGen.Tests;

public class DocMetaTests
{
    private static void CheckBody(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().Build();
        MarkdownBodyRules.EnsureNoTopLevelHeading(Markdig.Markdown.Parse(markdown, pipeline), "sample.md");
    }

    [Theory]
    [InlineData("getting-started")]
    [InlineData("control-flow")]
    [InlineData("api2")]
    [InlineData("a")]
    public void Validate_AcceptsUrlSafeStems(string stem) =>
        Assert.Equal(stem, DocSlug.Validate(stem, stem + ".md"));

    [Theory]
    [InlineData("Getting-Started")]  // uppercase
    [InlineData("getting started")]  // whitespace
    [InlineData("getting_started")]  // underscore
    [InlineData("getting.started")]  // dot
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--dash")]
    [InlineData("")]
    [InlineData("getting-started\n")]  // trailing newline: .NET's '$' would accept it, so the pattern anchors with \z
    public void Validate_RejectsUnsafeStems(string stem)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DocSlug.Validate(stem, "sample.md"));
        Assert.Contains("sample.md", ex.Message);
    }

    [Theory]
    [InlineData("# Title\n")]              // ATX h1
    [InlineData("Title\n=====\n")]         // setext h1
    [InlineData("#\tTitle\n")]             // tab-separated ATX h1
    [InlineData("> # Quoted title\n")]     // inside a blockquote
    [InlineData("- # In a list\n")]        // inside a list item
    public void EnsureNoTopLevelHeading_H1Forms_Throw(string markdown)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CheckBody(markdown));
        Assert.Contains("sample.md", ex.Message);
        Assert.Contains("h1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("## Section\n\n### Sub\n")]                  // h2 and below are allowed
    [InlineData("## Section\n=====\n")]                      // an '=' line after a heading is a paragraph, not a setext h1
    [InlineData("text\n\n    # indented code\n\nmore\n")]    // indented code block
    [InlineData("```text\n~~~\n# not a heading\n```\n")]     // '~~~' and '#' inside a fenced block
    [InlineData("- item\n===\n")]                            // list continuation line
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |\n")]        // table delimiter row
    [InlineData("text\n\n---\n\nmore\n")]                    // thematic break
    public void EnsureNoTopLevelHeading_NonH1Content_IsAllowed(string markdown) => CheckBody(markdown);
}

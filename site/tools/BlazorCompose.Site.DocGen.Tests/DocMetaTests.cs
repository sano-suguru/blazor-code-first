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
    [InlineData("Getting-Started")]  // 大文字
    [InlineData("getting started")]  // 空白
    [InlineData("getting_started")]  // アンダースコア
    [InlineData("getting.started")]  // ドット
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--dash")]
    [InlineData("")]
    public void Validate_RejectsUnsafeStems(string stem)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DocSlug.Validate(stem, "sample.md"));
        Assert.Contains("sample.md", ex.Message);
    }

    [Theory]
    [InlineData("# Title\n")]              // ATX h1
    [InlineData("Title\n=====\n")]         // setext h1
    [InlineData("#\tTitle\n")]             // タブ区切り ATX h1
    [InlineData("> # Quoted title\n")]     // 引用内
    [InlineData("- # In a list\n")]        // リスト内
    public void EnsureNoTopLevelHeading_H1Forms_Throw(string markdown)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CheckBody(markdown));
        Assert.Contains("sample.md", ex.Message);
        Assert.Contains("h1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("## Section\n\n### Sub\n")]                  // h2 以下は許可
    [InlineData("## Section\n=====\n")]                      // 見出し直後の '=' は段落
    [InlineData("text\n\n    # indented code\n\nmore\n")]    // インデントコードブロック
    [InlineData("```text\n~~~\n# not a heading\n```\n")]     // フェンス内の '~~~' と '#'
    [InlineData("- item\n===\n")]                            // リスト継続行
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |\n")]        // テーブル区切り
    [InlineData("text\n\n---\n\nmore\n")]                    // thematic break
    public void EnsureNoTopLevelHeading_NonH1Content_IsAllowed(string markdown) => CheckBody(markdown);
}

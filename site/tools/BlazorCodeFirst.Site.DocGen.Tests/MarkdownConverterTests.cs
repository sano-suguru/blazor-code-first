using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class MarkdownConverterTests
{
    [Fact]
    public void ToHtml_Heading_GetsGitHubSlugId()
    {
        string html = MarkdownConverter.ToHtml("# Getting Started");
        Assert.Contains("id=\"getting-started\"", html);
    }

    [Fact]
    public void ToHtml_DuplicateHeadings_GetUniqueSlugs()
    {
        string html = MarkdownConverter.ToHtml("# Notes\n\n# Notes");
        Assert.Contains("id=\"notes\"", html);
        Assert.Contains("id=\"notes-1\"", html);
    }

    [Fact]
    public void ToHtml_CSharpFence_GetsColorCodeClasses()
    {
        string html = MarkdownConverter.ToHtml("```csharp\nvar x = 1;\n```");
        // ColorCode class-based output wraps the block in the language class.
        Assert.Contains("class=\"csharp\"", html);
        // and emits at least one token span with a class.
        Assert.Contains("<span class=\"keyword\"", html);
    }

    [Fact]
    public void ToHtml_UnknownLanguageFence_DoesNotThrow_FallsBackToPlain()
    {
        string html = MarkdownConverter.ToHtml("```nosuchlang\nabc\n```");
        Assert.Contains("<pre", html);
        Assert.Contains("abc", html);
    }

    [Fact]
    public void ToHtml_EmptyFence_DoesNotThrow()
    {
        string html = MarkdownConverter.ToHtml("```\n```");
        Assert.Contains("<pre", html);
    }

    [Fact]
    public void ToHtml_WithKnownSlugs_RewritesRelativeLinksAndKeepsHighlighting()
    {
        string html = MarkdownConverter.ToHtml(
            "## Section\n\n[next](./control-flow.md)\n\n```csharp\nvar x = 1;\n```\n",
            new HashSet<string>(["control-flow"], StringComparer.Ordinal),
            "sample.md",
            "/docs");

        Assert.Contains("href=\"/docs/control-flow\"", html);
        // The AST path must not lose the ColorCode renderer registration.
        Assert.Contains("class=\"csharp\"", html);
        Assert.Contains("<span class=\"keyword\"", html);
        Assert.Contains("id=\"section\"", html);
    }

    [Fact]
    public void ToHtml_PipeTable_BecomesATable()
    {
        string html = MarkdownConverter.ToHtml("| a | b |\n| - | - |\n| 1 | 2 |\n");

        // css/app.css styles `.prose table` as a horizontal scroll container. Without the pipe-table
        // extension the same source renders as one <p> of literal pipes and that rule styles nothing.
        Assert.Contains("<table>", html);
        Assert.Contains("<th>a</th>", html);
        Assert.Contains("<td>1</td>", html);
    }

    [Fact]
    public void ToHtml_PipesInsideAFence_StayCode()
    {
        string html = MarkdownConverter.ToHtml("```csharp\nif (a || b) { }\n```");

        // The fence parser claims the block before the table parser sees the line, which is what a
        // rule written against the source text rather than the parsed document could not have done.
        Assert.DoesNotContain("<table>", html);
        Assert.Contains("class=\"csharp\"", html);
    }

    [Fact]
    public void ToHtml_SoftBreakBetweenJapaneseCharacters_JoinsWithoutASpace()
    {
        string html = MarkdownConverter.ToHtml("双方向バインディングは、1つの装飾\nです。\n");

        Assert.Contains("双方向バインディングは、1つの装飾です。", html);
    }

    [Fact]
    public void ToHtml_SoftBreakInsideAListItem_JoinsWithoutASpace()
    {
        string html = MarkdownConverter.ToHtml("- 数えます\n  （BCF3007）\n");

        // Fullwidth punctuation counts as CJK: the class cannot be kana and kanji alone.
        Assert.Contains("数えます（BCF3007）", html);
    }

    [Fact]
    public void ToHtml_SoftBreakBetweenJapaneseAndLatin_KeepsTheSpace()
    {
        string html = MarkdownConverter.ToHtml("その中身はふつうの\nBlazor です。\n");

        Assert.Contains("その中身はふつうの\nBlazor です。", html);
    }

    [Fact]
    public void ToHtml_SoftBreakBesideACodeSpan_KeepsTheSpace()
    {
        string html = MarkdownConverter.ToHtml("これらは\n`装飾` です。\n");

        // The Japanese edition sets a space around an inline <code> on purpose, so a code span is
        // not a CJK neighbour even when the run beside it is Japanese. The span holds Japanese here
        // precisely so the assertion turns on that rule: with a Latin identifier inside, the boundary
        // character would be Latin and the space would survive whether or not the rule existed.
        Assert.Contains("これらは\n<code>装飾</code>", html);
    }

    [Fact]
    public void ToHtml_SoftBreakBetweenLatinWords_KeepsTheSpace()
    {
        string html = MarkdownConverter.ToHtml("a decoration that writes\nthe value out.\n");

        Assert.Contains("writes\nthe value", html);
    }

    [Fact]
    public void ToHtml_HardBreakBetweenJapaneseCharacters_StaysABr()
    {
        string html = MarkdownConverter.ToHtml("1つの装飾  \nです。\n");

        Assert.Contains("<br />", html);
        Assert.DoesNotContain("1つの装飾です。", html);
    }

    [Fact]
    public void ToHtml_SoftBreakBeforeALinkWithJapaneseText_JoinsWithoutASpace()
    {
        string html = MarkdownConverter.ToHtml("ジェネレーターが読むものは\n[要素と装飾](https://example.test) です。\n");

        // The boundary character is the one the reader sees, so the pass reads through a link or an
        // emphasis into the literal inside it.
        Assert.Contains("読むものは<a", html);
        Assert.Contains(">要素と装飾</a>", html);
    }

    [Fact]
    public void ToHtml_SoftBreakBeforeAnEmphasisWithJapaneseText_JoinsWithoutASpace()
    {
        string html = MarkdownConverter.ToHtml("出力を見られません。だから\n*同じプロジェクト* で宣言します。\n");

        Assert.Contains("だから<em>同じプロジェクト</em>", html);
    }

    [Fact]
    public void ToHtml_WithKnownSlugs_EnforcesTheNoH1Rule()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => MarkdownConverter.ToHtml(
            "# Title\n", new HashSet<string>(StringComparer.Ordinal), "sample.md", "/docs"));

        Assert.Contains("sample.md", ex.Message);
    }
}

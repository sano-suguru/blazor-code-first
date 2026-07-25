using BlazorCompose.Site.DocGen;
using Xunit;

namespace BlazorCompose.Site.DocGen.Tests;

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
}

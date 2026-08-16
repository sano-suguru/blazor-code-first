using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class SnippetConverterTests
{
    [Fact]
    public void ToHtml_CSharpSource_HighlightsWithTheSameClassesAProseFenceUses()
    {
        string html = SnippetConverter.ToHtml("var x = 1;\n", "a/b.cs", "sample");

        Assert.Contains("<div class=\"csharp\">", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"keyword\">var</span>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_SourceEndingInANewline_DoesNotAddABlankLastLine()
    {
        string withNewline = SnippetConverter.ToHtml("var x = 1;\n", "a/b.cs", "sample");
        string without = SnippetConverter.ToHtml("var x = 1;", "a/b.cs", "sample");

        Assert.Equal(without, withNewline);
    }

    [Fact]
    public void ToHtml_CrlfSourceEndingInABlankLine_DoesNotAddABlankLastLine()
    {
        // Trimming '\n' alone would leave the carriage return, and the blank line with it.
        string crlf = SnippetConverter.ToHtml("var x = 1;\r\n\r\n", "a/b.cs", "sample");
        string lf = SnippetConverter.ToHtml("var x = 1;", "a/b.cs", "sample");

        Assert.Equal(lf, crlf);
    }

    [Fact]
    public void ToHtml_CrlfSource_ConvertsAsIfItWereLf()
    {
        string crlf = SnippetConverter.ToHtml("var x = 1;\r\nvar y = 2;\r\n", "a/b.cs", "sample");
        string lf = SnippetConverter.ToHtml("var x = 1;\nvar y = 2;\n", "a/b.cs", "sample");

        Assert.Equal(lf, crlf);
    }

    [Fact]
    public void ToHtml_SourceContainingABareBacktickLine_StaysInsideOneCodeBlock()
    {
        // A verbatim string may hold a line that is nothing but backticks. Against a three-backtick
        // wrapper that line closes the block, and the rest of the file renders as prose.
        // A comment-prefixed "// ```" cannot close a fence and so does not exercise this at all.
        string html = SnippetConverter.ToHtml("var md = @\"\n```\n\";\n", "a/b.cs", "sample");

        Assert.Equal(1, html.Split("<div class=\"csharp\">").Length - 1);
        Assert.Contains("var", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_UnmappedExtension_ThrowsNamingTheSnippetAndTheExtension()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SnippetConverter.ToHtml("hello", "a/b.md", "sample"));

        Assert.Contains("sample", error.Message, StringComparison.Ordinal);
        Assert.Contains(".md", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToHtml_NoExtension_ThrowsRatherThanGuessing()
    {
        Assert.Throws<InvalidOperationException>(
            () => SnippetConverter.ToHtml("hello", "a/b", "sample"));
    }

    [Fact]
    public void ToHtml_HtmlSource_HighlightsWithTheHtmlFenceClasses()
    {
        string html = SnippetConverter.ToHtml("<p class=\"lede\">hi</p>\n", "a/b.html", "sample");

        Assert.Contains("<div class=\"html\">", html, StringComparison.Ordinal);
        Assert.Contains("htmlElementName", html, StringComparison.Ordinal);
        Assert.Contains("htmlAttributeValue", html, StringComparison.Ordinal);
    }
}

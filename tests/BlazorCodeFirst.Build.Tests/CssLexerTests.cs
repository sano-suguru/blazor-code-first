using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public class CssLexerTests
{
    [Fact]
    public void SkipLiteral_returns_the_same_index_for_ordinary_text()
    {
        Assert.Equal(0, CssLexer.SkipLiteral(".a { color: red; }", 0));
    }

    [Fact]
    public void SkipLiteral_skips_a_double_quoted_string()
    {
        var css = "content: \"a { b } c\";";
        var start = css.IndexOf('"');

        var result = CssLexer.SkipLiteral(css, start);

        Assert.Equal(css.IndexOf('"', start + 1) + 1, result);
    }

    [Fact]
    public void SkipLiteral_skips_a_single_quoted_string()
    {
        var css = "content: 'a { b } c';";
        var start = css.IndexOf('\'');

        var result = CssLexer.SkipLiteral(css, start);

        Assert.Equal(css.IndexOf('\'', start + 1) + 1, result);
    }

    [Fact]
    public void SkipLiteral_respects_a_backslash_escaped_quote_inside_a_string()
    {
        var css = "content: \"a \\\" b\";";
        var start = css.IndexOf('"');

        var result = CssLexer.SkipLiteral(css, start);

        // The escaped quote at index start+3 must not end the string; the real closing quote is
        // the last '"' in the literal.
        Assert.Equal(css.LastIndexOf('"') + 1, result);
    }

    [Fact]
    public void SkipLiteral_returns_the_string_length_for_an_unterminated_string()
    {
        var css = "content: \"never closes";

        var result = CssLexer.SkipLiteral(css, css.IndexOf('"'));

        Assert.Equal(css.Length, result);
    }

    [Fact]
    public void SkipLiteral_skips_a_block_comment_containing_braces_and_at_signs()
    {
        var css = "/* @media { fake } */ .a { color: red; }";

        var result = CssLexer.SkipLiteral(css, 0);

        Assert.Equal(css.IndexOf("*/", System.StringComparison.Ordinal) + 2, result);
    }

    [Fact]
    public void SkipLiteral_returns_the_string_length_for_an_unterminated_comment()
    {
        var css = "/* never closes";

        var result = CssLexer.SkipLiteral(css, 0);

        Assert.Equal(css.Length, result);
    }

    [Fact]
    public void SkipLiteral_does_not_treat_a_lone_slash_as_a_comment_start()
    {
        Assert.Equal(0, CssLexer.SkipLiteral("/deep/", 0));
    }
}

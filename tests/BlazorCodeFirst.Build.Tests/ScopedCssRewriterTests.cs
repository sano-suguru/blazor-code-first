using System;
using BlazorCodeFirst.Build;
using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public class ScopedCssRewriterTests
{
    [Fact]
    public void Rewrite_appends_scope_to_a_single_class_selector()
    {
        var css = ".my-component {\n    color: red;\n}\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "bcf-abcd1234", out var errors);

        Assert.Empty(errors);
        Assert.Equal(".my-component[bcf-abcd1234] {\n    color: red;\n}\n", result);
    }

    [Fact]
    public void Rewrite_appends_scope_to_every_selector_in_a_comma_separated_list()
    {
        var css = ".a, .b {\n    color: red;\n}\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "bcf-abcd1234", out var errors);

        Assert.Empty(errors);
        Assert.Equal(".a[bcf-abcd1234], .b[bcf-abcd1234] {\n    color: red;\n}\n", result);
    }

    [Fact]
    public void Rewrite_handles_multiple_rule_blocks()
    {
        var css = ".a {\n    color: red;\n}\n.b {\n    color: blue;\n}\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "bcf-abcd1234", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            ".a[bcf-abcd1234] {\n    color: red;\n}\n.b[bcf-abcd1234] {\n    color: blue;\n}\n",
            result);
    }

    [Theory]
    [InlineData("@media (min-width: 640px) { .a { color: red; } }")]
    [InlineData("@keyframes fade { from { opacity: 0; } }")]
    [InlineData("@import url('other.css');")]
    public void Rewrite_throws_for_top_level_at_rules(string css)
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => ScopedCssRewriter.Rewrite("file.css", css, "bcf-abcd1234", out _));

        Assert.Contains("at-rule", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

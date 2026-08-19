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

    [Fact]
    public void HandlesEmptyFile()
    {
        var result = ScopedCssRewriter.Rewrite("file.css", string.Empty, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void AddsScopeAfterSelector()
    {
        var css = "\n    .myclass { color: red; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal("\n    .myclass[TestScope] { color: red; }\n", result);
    }

    [Fact]
    public void HandlesMultipleSelectors()
    {
        var css = "\n    .first, .second { color: red; }\n    .third { color: blue; }\n    :root { color: green; }\n    * { color: white; }\n    #some-id { color: yellow; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    .first[TestScope], .second[TestScope] { color: red; }\n    .third[TestScope] { color: blue; }\n    :root[TestScope] { color: green; }\n    *[TestScope] { color: white; }\n    #some-id[TestScope] { color: yellow; }\n",
            result);
    }

    [Fact]
    public void HandlesComplexSelectors()
    {
        var css = "\n    .first div > li, body .second:not(.fancy)[attr~=whatever] { color: red; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    .first div > li[TestScope], body .second:not(.fancy)[attr~=whatever][TestScope] { color: red; }\n",
            result);
    }

    [Fact]
    public void HandlesSpacesAndCommentsWithinSelectors()
    {
        var css = "\n    .first /* space at end {} */ div , .myclass /* comment at end */ { color: red; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    .first /* space at end {} */ div[TestScope] , .myclass[TestScope] /* comment at end */ { color: red; }\n",
            result);
    }

    [Fact]
    public void HandlesPseudoClasses()
    {
        var css = "\n    a:fake-pseudo-class { color: red; }\n    a:focus b:hover { color: green; }\n    tr:nth-child(4n + 1) { color: blue; }\n    a:has(b > c) { color: yellow; }\n    a:last-child > ::deep b { color: pink; }\n    a:not(#something) { color: purple; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    a:fake-pseudo-class[TestScope] { color: red; }\n    a:focus b:hover[TestScope] { color: green; }\n    tr:nth-child(4n + 1)[TestScope] { color: blue; }\n    a:has(b > c)[TestScope] { color: yellow; }\n    a:last-child[TestScope] >  b { color: pink; }\n    a:not(#something)[TestScope] { color: purple; }\n",
            result);
    }

    [Fact]
    public void HandlesPseudoElements()
    {
        var css = "\n    a::before { content: \"x\"; }\n    a::after::placeholder { content: \"y\"; }\n    custom-element::part(foo) { content: \"z\"; }\n    a::before > ::deep another { content: \"w\"; }\n    a::fake-PsEuDo-element { content: \"v\"; }\n    ::selection { content: \"u\"; }\n    other, ::selection { content: \"t\"; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    a[TestScope]::before { content: \"x\"; }\n    a[TestScope]::after::placeholder { content: \"y\"; }\n    custom-element[TestScope]::part(foo) { content: \"z\"; }\n    a[TestScope]::before >  another { content: \"w\"; }\n    a[TestScope]::fake-PsEuDo-element { content: \"v\"; }\n    [TestScope]::selection { content: \"u\"; }\n    other[TestScope], [TestScope]::selection { content: \"t\"; }\n",
            result);
    }

    [Fact]
    public void HandlesSingleColonPseudoElements()
    {
        var css = "\n    a:after { content: \"x\"; }\n    a:before { content: \"x\"; }\n    a:first-letter { content: \"x\"; }\n    a:first-line { content: \"x\"; }\n    a:AFTER { content: \"x\"; }\n    a:not(something):before { content: \"x\"; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    a[TestScope]:after { content: \"x\"; }\n    a[TestScope]:before { content: \"x\"; }\n    a[TestScope]:first-letter { content: \"x\"; }\n    a[TestScope]:first-line { content: \"x\"; }\n    a[TestScope]:AFTER { content: \"x\"; }\n    a:not(something)[TestScope]:before { content: \"x\"; }\n",
            result);
    }

    [Fact]
    public void RespectsDeepCombinator()
    {
        var css = "\n    .first ::deep .second { color: red; }\n    a ::deep b, c ::deep d { color: blue; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    .first[TestScope]  .second { color: red; }\n    a[TestScope]  b, c[TestScope]  d { color: blue; }\n",
            result);
    }

    [Fact]
    public void RespectsDeepCombinatorWithDirectDescendant()
    {
        var css = "\n    a  >  ::deep b { color: red; }\n    c ::deep  >  d { color: blue; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    a[TestScope]  >   b { color: red; }\n    c[TestScope]   >  d { color: blue; }\n",
            result);
    }

    [Fact]
    public void RespectsDeepCombinatorWithAdjacentSibling()
    {
        var css = "\n    a + ::deep b { color: red; }\n    c ::deep + d { color: blue; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    a[TestScope] +  b { color: red; }\n    c[TestScope]  + d { color: blue; }\n",
            result);
    }

    [Fact]
    public void RespectsDeepCombinatorWithGeneralSibling()
    {
        var css = "\n    a ~ ::deep b { color: red; }\n    c ::deep ~ d { color: blue; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    a[TestScope] ~  b { color: red; }\n    c[TestScope]  ~ d { color: blue; }\n",
            result);
    }

    [Fact]
    public void IgnoresMultipleDeepCombinators()
    {
        var css = "\n    .first ::deep .second ::deep .third { color:red; }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    .first[TestScope]  .second ::deep .third { color:red; }\n",
            result);
    }

    [Fact]
    public void RespectsDeepCombinatorWithSpacesAndComments()
    {
        var css = "\n    .a .b /* comment ::deep 1 */  ::deep  /* comment ::deep 2 */  .c /* ::deep */ .d { color: red; }\n    ::deep * { color: blue; } /* Leading deep combinator */\n    another ::deep { color: green }  /* Trailing deep combinator */\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    .a .b[TestScope] /* comment ::deep 1 */    /* comment ::deep 2 */  .c /* ::deep */ .d { color: red; }\n    [TestScope] * { color: blue; } /* Leading deep combinator */\n    another[TestScope]  { color: green }  /* Trailing deep combinator */\n",
            result);
    }

    [Fact]
    public void HandlesAtBlocks()
    {
        var css = "\n    .myclass { color: red; }\n\n    @media only screen and (max-width: 600px) {\n        .another .thing {\n            content: 'This should not be a selector: .fake-selector { color: red }'\n        }\n    }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    .myclass[TestScope] { color: red; }\n\n    @media only screen and (max-width: 600px) {\n        .another .thing[TestScope] {\n            content: 'This should not be a selector: .fake-selector { color: red }'\n        }\n    }\n",
            result);
    }

    [Fact]
    public void RejectsImportStatements()
    {
        var css = "\n    @import \"basic-import.css\";\n    @import \"import-with-media-type.css\" print;\n    @import \"import-with-media-query.css\" screen and (orientation:landscape);\n    @ImPoRt /* comment */ \"scheme://path/to/complex-import\" /* another-comment */ screen;\n    @otheratrule \"should-not-cause-error.css\";\n    /* @import \"should-be-ignored-because-it-is-in-a-comment.css\"; */\n    .myclass { color: red; }\n";

        ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Equal(4, errors.Count);
        Assert.Equal(
            "file.css(2,5): @import rules are not supported within scoped CSS files because the loading order would be undefined. @import may only be placed in non-scoped CSS files.",
            errors[0].ToString());
        Assert.Equal(
            "file.css(3,5): @import rules are not supported within scoped CSS files because the loading order would be undefined. @import may only be placed in non-scoped CSS files.",
            errors[1].ToString());
        Assert.Equal(
            "file.css(4,5): @import rules are not supported within scoped CSS files because the loading order would be undefined. @import may only be placed in non-scoped CSS files.",
            errors[2].ToString());
        Assert.Equal(
            "file.css(5,5): @import rules are not supported within scoped CSS files because the loading order would be undefined. @import may only be placed in non-scoped CSS files.",
            errors[3].ToString());
    }

    [Fact]
    public void AddsScopeToKeyframeNames()
    {
        var css = "\n    @keyframes my-animation { /* whatever */ }\n";

        var result = ScopedCssRewriter.Rewrite("file.css", css, "TestScope", out var errors);

        Assert.Empty(errors);
        Assert.Equal(
            "\n    @keyframes my-animation-TestScope { /* whatever */ }\n",
            result);
    }
}

using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public class ScopedCssRewriterTests
{
    private static void AssertRewrite(string css, string expected, string scope = "TestScope")
    {
        var result = ScopedCssRewriter.Rewrite("file.css", css, scope, out var errors);

        Assert.Empty(errors);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Rewrite_appends_scope_to_a_single_class_selector() =>
        AssertRewrite(
            ".my-component {\n    color: red;\n}\n",
            ".my-component[bcf-abcd1234] {\n    color: red;\n}\n",
            scope: "bcf-abcd1234");

    [Fact]
    public void Rewrite_appends_scope_to_every_selector_in_a_comma_separated_list() =>
        AssertRewrite(
            ".a, .b {\n    color: red;\n}\n",
            ".a[bcf-abcd1234], .b[bcf-abcd1234] {\n    color: red;\n}\n",
            scope: "bcf-abcd1234");

    [Fact]
    public void Rewrite_handles_multiple_rule_blocks() =>
        AssertRewrite(
            ".a {\n    color: red;\n}\n.b {\n    color: blue;\n}\n",
            ".a[bcf-abcd1234] {\n    color: red;\n}\n.b[bcf-abcd1234] {\n    color: blue;\n}\n",
            scope: "bcf-abcd1234");

    [Fact]
    public void HandlesEmptyFile() => AssertRewrite(string.Empty, string.Empty);

    [Fact]
    public void AddsScopeAfterSelector() =>
        AssertRewrite(
            "\n    .myclass { color: red; }\n",
            "\n    .myclass[TestScope] { color: red; }\n");

    [Fact]
    public void HandlesMultipleSelectors() =>
        AssertRewrite(
            "\n    .first, .second { color: red; }\n    .third { color: blue; }\n    :root { color: green; }\n    * { color: white; }\n    #some-id { color: yellow; }\n",
            "\n    .first[TestScope], .second[TestScope] { color: red; }\n    .third[TestScope] { color: blue; }\n    :root[TestScope] { color: green; }\n    *[TestScope] { color: white; }\n    #some-id[TestScope] { color: yellow; }\n");

    [Fact]
    public void HandlesComplexSelectors() =>
        AssertRewrite(
            "\n    .first div > li, body .second:not(.fancy)[attr~=whatever] { color: red; }\n",
            "\n    .first div > li[TestScope], body .second:not(.fancy)[attr~=whatever][TestScope] { color: red; }\n");

    [Fact]
    public void HandlesSpacesAndCommentsWithinSelectors() =>
        AssertRewrite(
            "\n    .first /* space at end {} */ div , .myclass /* comment at end */ { color: red; }\n",
            "\n    .first /* space at end {} */ div[TestScope] , .myclass[TestScope] /* comment at end */ { color: red; }\n");

    [Fact]
    public void HandlesPseudoClasses() =>
        AssertRewrite(
            "\n    a:fake-pseudo-class { color: red; }\n    a:focus b:hover { color: green; }\n    tr:nth-child(4n + 1) { color: blue; }\n    a:has(b > c) { color: yellow; }\n    a:last-child > ::deep b { color: pink; }\n    a:not(#something) { color: purple; }\n",
            "\n    a:fake-pseudo-class[TestScope] { color: red; }\n    a:focus b:hover[TestScope] { color: green; }\n    tr:nth-child(4n + 1)[TestScope] { color: blue; }\n    a:has(b > c)[TestScope] { color: yellow; }\n    a:last-child[TestScope] >  b { color: pink; }\n    a:not(#something)[TestScope] { color: purple; }\n");

    [Fact]
    public void HandlesPseudoElements() =>
        AssertRewrite(
            "\n    a::before { content: \"x\"; }\n    a::after::placeholder { content: \"y\"; }\n    custom-element::part(foo) { content: \"z\"; }\n    a::before > ::deep another { content: \"w\"; }\n    a::fake-PsEuDo-element { content: \"v\"; }\n    ::selection { content: \"u\"; }\n    other, ::selection { content: \"t\"; }\n",
            "\n    a[TestScope]::before { content: \"x\"; }\n    a[TestScope]::after::placeholder { content: \"y\"; }\n    custom-element[TestScope]::part(foo) { content: \"z\"; }\n    a[TestScope]::before >  another { content: \"w\"; }\n    a[TestScope]::fake-PsEuDo-element { content: \"v\"; }\n    [TestScope]::selection { content: \"u\"; }\n    other[TestScope], [TestScope]::selection { content: \"t\"; }\n");

    [Fact]
    public void HandlesSingleColonPseudoElements() =>
        AssertRewrite(
            "\n    a:after { content: \"x\"; }\n    a:before { content: \"x\"; }\n    a:first-letter { content: \"x\"; }\n    a:first-line { content: \"x\"; }\n    a:AFTER { content: \"x\"; }\n    a:not(something):before { content: \"x\"; }\n",
            "\n    a[TestScope]:after { content: \"x\"; }\n    a[TestScope]:before { content: \"x\"; }\n    a[TestScope]:first-letter { content: \"x\"; }\n    a[TestScope]:first-line { content: \"x\"; }\n    a[TestScope]:AFTER { content: \"x\"; }\n    a:not(something)[TestScope]:before { content: \"x\"; }\n");

    [Fact]
    public void RespectsDeepCombinator() =>
        AssertRewrite(
            "\n    .first ::deep .second { color: red; }\n    a ::deep b, c ::deep d { color: blue; }\n",
            "\n    .first[TestScope]  .second { color: red; }\n    a[TestScope]  b, c[TestScope]  d { color: blue; }\n");

    [Fact]
    public void RespectsDeepCombinatorWithDirectDescendant() =>
        AssertRewrite(
            "\n    a  >  ::deep b { color: red; }\n    c ::deep  >  d { color: blue; }\n",
            "\n    a[TestScope]  >   b { color: red; }\n    c[TestScope]   >  d { color: blue; }\n");

    [Fact]
    public void RespectsDeepCombinatorWithAdjacentSibling() =>
        AssertRewrite(
            "\n    a + ::deep b { color: red; }\n    c ::deep + d { color: blue; }\n",
            "\n    a[TestScope] +  b { color: red; }\n    c[TestScope]  + d { color: blue; }\n");

    [Fact]
    public void RespectsDeepCombinatorWithGeneralSibling() =>
        AssertRewrite(
            "\n    a ~ ::deep b { color: red; }\n    c ::deep ~ d { color: blue; }\n",
            "\n    a[TestScope] ~  b { color: red; }\n    c[TestScope]  ~ d { color: blue; }\n");

    [Fact]
    public void IgnoresMultipleDeepCombinators() =>
        AssertRewrite(
            "\n    .first ::deep .second ::deep .third { color:red; }\n",
            "\n    .first[TestScope]  .second ::deep .third { color:red; }\n");

    [Fact]
    public void DoesNotTreatPseudoElementsStartingWithDeepAsTheDeepCombinator() =>
        AssertRewrite(
            "\n    a::deepwater { color: red; }\n",
            "\n    a[TestScope]::deepwater { color: red; }\n");

    [Fact]
    public void RespectsDeepCombinatorWithSpacesAndComments() =>
        AssertRewrite(
            "\n    .a .b /* comment ::deep 1 */  ::deep  /* comment ::deep 2 */  .c /* ::deep */ .d { color: red; }\n    ::deep * { color: blue; } /* Leading deep combinator */\n    another ::deep { color: green }  /* Trailing deep combinator */\n",
            "\n    .a .b[TestScope] /* comment ::deep 1 */    /* comment ::deep 2 */  .c /* ::deep */ .d { color: red; }\n    [TestScope] * { color: blue; } /* Leading deep combinator */\n    another[TestScope]  { color: green }  /* Trailing deep combinator */\n");

    [Fact]
    public void HandlesAtBlocks() =>
        AssertRewrite(
            "\n    .myclass { color: red; }\n\n    @media only screen and (max-width: 600px) {\n        .another .thing {\n            content: 'This should not be a selector: .fake-selector { color: red }'\n        }\n    }\n",
            "\n    .myclass[TestScope] { color: red; }\n\n    @media only screen and (max-width: 600px) {\n        .another .thing[TestScope] {\n            content: 'This should not be a selector: .fake-selector { color: red }'\n        }\n    }\n");

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

    [Theory]
    [InlineData("")]
    [InlineData("-webkit-")]
    [InlineData("-moz-")]
    [InlineData("-o-")]
    [InlineData("-ms-")]
    public void AddsScopeToKeyframeNames(string prefix) =>
        AssertRewrite(
            $"\n    @{prefix}keyframes fade {{ from{{opacity:0}} to{{opacity:1}} }}\n",
            $"\n    @{prefix}keyframes fade-TestScope {{ from{{opacity:0}} to{{opacity:1}} }}\n");

    [Fact]
    public void RewritesAnimationNamesWhenMatchingKnownKeyframes() =>
        AssertRewrite(
            "\n    .myclass {\n        color: red;\n        animation: /* ignore comment */ my-animation 1s infinite;\n    }\n\n    .another-thing { animation-name: different-animation; }\n\n    h1 { animation: unknown-animation; } /* Should not be scoped */\n\n    @keyframes my-animation { /* whatever */ }\n    @keyframes different-animation { /* whatever */ }\n    @keyframes unused-animation { /* whatever */ }\n",
            "\n    .myclass[TestScope] {\n        color: red;\n        animation: /* ignore comment */ my-animation-TestScope 1s infinite;\n    }\n\n    .another-thing[TestScope] { animation-name: different-animation-TestScope; }\n\n    h1[TestScope] { animation: unknown-animation; } /* Should not be scoped */\n\n    @keyframes my-animation-TestScope { /* whatever */ }\n    @keyframes different-animation-TestScope { /* whatever */ }\n    @keyframes unused-animation-TestScope { /* whatever */ }\n");

    [Fact]
    public void RewritesMultipleAnimationNames() =>
        AssertRewrite(
            "\n    .myclass1 { animation-name: my-animation , different-animation }\n    .myclass2 { animation: 4s linear 0s alternate my-animation infinite, different-animation 0s }\n    @keyframes my-animation { }\n    @keyframes different-animation { }\n",
            "\n    .myclass1[TestScope] { animation-name: my-animation-TestScope , different-animation-TestScope }\n    .myclass2[TestScope] { animation: 4s linear 0s alternate my-animation-TestScope infinite, different-animation-TestScope 0s }\n    @keyframes my-animation-TestScope { }\n    @keyframes different-animation-TestScope { }\n");

    [Fact]
    public void RewritesAnimationNamesWhenMatchingVendorPrefixedKeyframes() =>
        AssertRewrite(
            "\n    .myclass { animation: fade 1s infinite; -webkit-animation: fade 1s infinite; -webkit-animation-name: fade; }\n    @-webkit-keyframes fade { from{opacity:0} to{opacity:1} }\n",
            "\n    .myclass[TestScope] { animation: fade-TestScope 1s infinite; -webkit-animation: fade-TestScope 1s infinite; -webkit-animation-name: fade-TestScope; }\n    @-webkit-keyframes fade-TestScope { from{opacity:0} to{opacity:1} }\n");

    [Fact]
    public void AddsScopeToQuotedKeyframeNames() =>
        AssertRewrite(
            "\n    @keyframes \"spin\" { /* whatever */ }\n",
            "\n    @keyframes \"spin-TestScope\" { /* whatever */ }\n");

    [Fact]
    public void RewritesQuotedAnimationNamesWhenMatchingQuotedKeyframes() =>
        AssertRewrite(
            "\n    .myclass { animation: \"spin\" 2s linear; }\n    @keyframes \"spin\" { }\n",
            "\n    .myclass[TestScope] { animation: \"spin-TestScope\" 2s linear; }\n    @keyframes \"spin-TestScope\" { }\n");

    [Fact]
    public void MatchesAQuotedKeyframesNameAgainstABareAnimationNameValue() =>
        AssertRewrite(
            "\n    .myclass { animation-name: spin; }\n    @keyframes \"spin\" { }\n",
            "\n    .myclass[TestScope] { animation-name: spin-TestScope; }\n    @keyframes \"spin-TestScope\" { }\n");
}

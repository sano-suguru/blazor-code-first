using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>Holds `css/app.css` to the lists it has to keep in step with.</summary>
public class StylesheetTests
{
    /// <summary>The one selector that is not per-language, which the rest of the rule follows.</summary>
    private const string MarginReset = ".slab > pre";

    public static TheoryData<string> FenceLanguages()
    {
        var data = new TheoryData<string>();
        foreach (string language in SnippetConverter.Languages.Values.Distinct().Order(StringComparer.Ordinal))
        {
            data.Add(language);
        }

        return data;
    }

    // A figure's markup is `<div class="{language}"><pre>`, so the margin reset needs one selector
    // per fence language. Adding `.html` to the extension map without adding one here left that
    // figure with the UA `pre` margin: measured, it stood 26px taller than the C# figure beside it in
    // the same pair, and nothing failed -- the browser suite reads overflow and contrast, not box
    // height (#400).
    [Theory]
    [MemberData(nameof(FenceLanguages))]
    public void AppCss_ResetsThePreMarginForEveryFenceLanguage(string language)
    {
        string css = File.ReadAllText(
            RepositoryPath.From(Path.Combine("site", "BlazorCodeFirst.Site", "wwwroot", "css", "app.css")));

        int start = css.IndexOf(MarginReset, StringComparison.Ordinal);
        Assert.True(start >= 0, $"css/app.css carries no '{MarginReset}' rule to read the selector list off.");

        // The selector list of that one rule, rather than the file: a per-language selector that
        // landed in some other rule would reset a margin the slab is not the one paying.
        string selectors = css[start..css.IndexOf('{', start)];

        Assert.Contains($".{language} > pre", selectors, StringComparison.Ordinal);
    }

    // The container class is the whole mechanism: DocGen accepts one info string and renders it as a
    // class, and nothing else makes the block look like a warning. Painted under `.prose`, because
    // that is where a document's HTML lands; a rule written without it would style nothing.
    [Fact]
    public void AppCss_PaintsTheContainerClassDocGenAccepts()
    {
        string css = File.ReadAllText(
            RepositoryPath.From(Path.Combine("site", "BlazorCodeFirst.Site", "wwwroot", "css", "app.css")));

        Assert.Contains($".prose .{MarkdownBodyRules.WarningContainer} {{", css, StringComparison.Ordinal);
    }
}

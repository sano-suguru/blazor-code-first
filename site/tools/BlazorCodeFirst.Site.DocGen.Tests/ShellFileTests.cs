using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class ShellFileTests
{
    private const string Common =
        "name: English\n" +
        "index-title: Documentation\n" +
        "index-lead: Every document, in reading order.\n" +
        "rail-heading: Guide\n" +
        "language-label: Language\n";

    private const string Stale =
        "stale-notice: This translation is behind.\n" +
        "stale-link: Read the English page\n";

    private static ShellStrings ParseCanonical(string keys) =>
        ShellFile.Parse($"---\n{keys}---\n", "shell.yml", isCanonical: true);

    private static ShellStrings ParseTranslation(string keys) =>
        ShellFile.Parse($"---\n{keys}---\n", "ja/shell.yml", isCanonical: false);

    [Fact]
    public void Parse_ReadsEveryKey()
    {
        var shell = ParseTranslation(Common + Stale);

        Assert.Equal("English", shell.Name);
        Assert.Equal("Documentation", shell.IndexTitle);
        Assert.Equal("Every document, in reading order.", shell.IndexLead);
        Assert.Equal("Guide", shell.RailHeading);
        Assert.Equal("Language", shell.LanguageLabel);
        Assert.Equal("This translation is behind.", shell.StaleNotice);
        Assert.Equal("Read the English page", shell.StaleLink);
    }

    [Fact]
    public void Parse_CanonicalHasNoStaleText()
    {
        var shell = ParseCanonical(Common);

        Assert.Null(shell.StaleNotice);
        Assert.Null(shell.StaleLink);
    }

    [Fact]
    public void Parse_MissingKey_ThrowsNamingIt()
    {
        string withoutRailHeading = Common.Replace("rail-heading: Guide\n", "", StringComparison.Ordinal);

        var ex = Assert.Throws<InvalidOperationException>(() => ParseCanonical(withoutRailHeading));

        // A missing string must not fall back to the canonical language: a translation that silently
        // shows one English word among Japanese ones is the failure this file exists to prevent.
        Assert.Contains("rail-heading", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TranslationMissingStaleText_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ParseTranslation(Common));

        // Saying it is behind is the one thing a stale page has to do.
        Assert.Contains("stale-notice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_CanonicalDeclaringStaleText_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ParseCanonical(Common + Stale));

        Assert.Contains("stale-notice", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_UnknownKey_ThrowsNamingIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ParseCanonical(Common + "footer: (c) 2026\n"));

        Assert.Contains("footer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RepeatedKey_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ParseCanonical(Common + "rail-heading: Contents\n"));

        Assert.Contains("rail-heading", ex.Message, StringComparison.Ordinal);
        Assert.Contains("more than once", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptyValue_Throws()
    {
        string blankLead = Common.Replace("index-lead: Every document, in reading order.\n", "index-lead:\n", StringComparison.Ordinal);

        var ex = Assert.Throws<InvalidOperationException>(() => ParseCanonical(blankLead));

        Assert.Contains("index-lead", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TextAfterTheBlock_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellFile.Parse($"---\n{Common}---\n\n## Not a document\n", "shell.yml", isCanonical: true));

        // This file is the block and nothing else. Prose here would render nowhere, so accepting it
        // would let an author write a paragraph no reader ever sees.
        Assert.Contains("after the closing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NoOpeningFence_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellFile.Parse(Common, "shell.yml", isCanonical: true));

        Assert.Contains("shell.yml", ex.Message, StringComparison.Ordinal);
    }
}

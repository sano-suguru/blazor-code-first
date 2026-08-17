using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class ShellFileTests
{
    private const string Common =
        "name: English\n" +
        "index-title: Documentation\n" +
        "index-description: The guide, in reading order.\n" +
        "index-lead: Every document, in reading order.\n" +
        "rail-heading: Guide\n" +
        "language-label: Language\n" +
        "anchor-filter-label: Jump to a diagnostic\n" +
        "group-start: Start here\n" +
        "group-write: Writing views\n" +
        "group-reference: Reference\n";

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
        Assert.Equal("The guide, in reading order.", shell.IndexDescription);
        Assert.Equal("Every document, in reading order.", shell.IndexLead);
        Assert.Equal("Guide", shell.RailHeading);
        Assert.Equal("Language", shell.LanguageLabel);
        Assert.Equal("This translation is behind.", shell.StaleNotice);
        Assert.Equal("Read the English page", shell.StaleLink);
    }

    [Fact]
    public void Parse_ReadsAGroupHeadingForEveryGroup()
    {
        var shell = ParseCanonical(Common);

        Assert.Equal(DocGroup.All.Length, shell.Groups.Count);
        Assert.Equal("Start here", shell.Groups[DocGroup.Start]);
        Assert.Equal("Writing views", shell.Groups[DocGroup.Write]);
        Assert.Equal("Reference", shell.Groups[DocGroup.Reference]);
    }

    [Theory]
    [InlineData(DocGroup.Start)]
    [InlineData(DocGroup.Write)]
    [InlineData(DocGroup.Reference)]
    public void Parse_MissingOneGroupHeading_Throws(string group)
    {
        // Every group carries a heading whether or not this language has a document in it. A missing
        // one would put an English word into a translated rail the first time a document moved.
        string key = ShellFile.GroupKey(group);
        string withoutIt = string.Concat(
            Common.Split('\n').Where(l => !l.StartsWith(key + ":", StringComparison.Ordinal))
                .Select(l => l.Length == 0 ? l : l + "\n"));

        var ex = Assert.Throws<InvalidOperationException>(() => ParseCanonical(withoutIt));

        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
        Assert.Contains("required", ex.Message, StringComparison.OrdinalIgnoreCase);
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
    public void Parse_MissingIndexDescription_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ParseCanonical(WithoutIndexDescription()));

        Assert.Contains("index-description", ex.Message, StringComparison.Ordinal);
        Assert.Contains("required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_TranslationMissingIndexDescription_Throws()
    {
        // Required of a translation as well, unlike the two stale keys. An index whose description
        // fell back to English would be the one page in the edition describing itself in the wrong
        // language to a search result.
        var ex = Assert.Throws<InvalidOperationException>(
            () => ParseTranslation(WithoutIndexDescription() + Stale));

        Assert.Contains("index-description", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_IndexDescriptionOverTheCanonicalLimit_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ParseCanonical(WithIndexDescription(new string('x', 161))));

        Assert.Contains("index-description", ex.Message, StringComparison.Ordinal);
        Assert.Contains("161", ex.Message, StringComparison.Ordinal);
        Assert.Contains("160", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_IndexDescriptionOverTheTranslationLimit_Throws()
    {
        // The same length the canonical edition accepts, rejected here: a translation's limit is the
        // narrower one, because a full-width character takes about twice the width in a result.
        var ex = Assert.Throws<InvalidOperationException>(
            () => ParseTranslation(WithIndexDescription(new string('x', 91)) + Stale));

        Assert.Contains("91", ex.Message, StringComparison.Ordinal);
        Assert.Contains("90", ex.Message, StringComparison.Ordinal);
    }

    private const string IndexDescriptionLine = "index-description: The guide, in reading order.\n";

    private static string WithoutIndexDescription() =>
        Common.Replace(IndexDescriptionLine, "", StringComparison.Ordinal);

    private static string WithIndexDescription(string value) =>
        Common.Replace(IndexDescriptionLine, $"index-description: {value}\n", StringComparison.Ordinal);

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

using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class SnippetManifestTests
{
    private const string FileName = "manifest";

    [Fact]
    public void Parse_DeclaredSnippets_KeepsManifestOrder()
    {
        var entries = SnippetManifest.Parse(
            "---\ncounter: ../a/CounterPage.cs\nhero: hero.cs\n---\n", FileName);

        Assert.Collection(
            entries,
            e => Assert.Equal(new SnippetEntry("counter", "../a/CounterPage.cs"), e),
            e => Assert.Equal(new SnippetEntry("hero", "hero.cs"), e));
    }

    [Fact]
    public void Parse_KebabCaseName_MapsEachPartToPascalCase()
    {
        var entries = SnippetManifest.Parse("---\ndesign-time: d.cs\n---\n", FileName);

        Assert.Equal("DesignTime", Assert.Single(entries).MemberName);
    }

    [Fact]
    public void Parse_NameWithDigitInsideAPart_IsAccepted()
    {
        var entries = SnippetManifest.Parse("---\nbcf3016-figure: d.cs\n---\n", FileName);

        Assert.Equal("Bcf3016Figure", Assert.Single(entries).MemberName);
    }

    [Theory]
    [InlineData("Hero")]        // uppercase
    [InlineData("my_snippet")]  // underscore
    [InlineData("1st")]         // opens with a digit
    [InlineData("a--b")]        // empty part
    [InlineData("-a")]          // leading separator
    [InlineData("a-")]          // trailing separator
    public void Parse_NameThatIsNotKebabCase_Throws(string name)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SnippetManifest.Parse($"---\n{name}: a.cs\n---\n", FileName));

        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_SameNameDeclaredTwice_ThrowsNamingIt()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SnippetManifest.Parse("---\nhero: a.cs\nhero: b.cs\n---\n", FileName));

        Assert.Contains("hero", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptyPath_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => SnippetManifest.Parse("---\nhero:\n---\n", FileName));
    }

    [Fact]
    public void Parse_TextAfterTheClosingFence_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => SnippetManifest.Parse("---\nhero: a.cs\n---\nstray\n", FileName));

        Assert.Contains("block and nothing else", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NoSnippetsDeclared_ReturnsEmpty()
    {
        Assert.Empty(SnippetManifest.Parse("---\n---\n", FileName));
    }

    /// <summary>
    /// The shared block parser is reached by three kinds of file, so it takes the noun from its
    /// caller rather than fixing one. Without this the manifest's errors read "Invalid document
    /// 'manifest'", which sends an author looking through site/content for a file that is not there.
    /// </summary>
    [Fact]
    public void Parse_AnyFailure_NamesTheFileAsASnippetManifest()
    {
        var fromTheBlockParser = Assert.Throws<InvalidOperationException>(
            () => SnippetManifest.Parse("no fence here\n", FileName));
        var fromTheManifestRules = Assert.Throws<InvalidOperationException>(
            () => SnippetManifest.Parse("---\nHero: a.cs\n---\n", FileName));

        Assert.StartsWith(
            "Invalid snippet manifest 'manifest'", fromTheBlockParser.Message, StringComparison.Ordinal);
        Assert.StartsWith(
            "Invalid snippet manifest 'manifest'", fromTheManifestRules.Message, StringComparison.Ordinal);
    }
}

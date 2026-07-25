using BlazorCompose.Site.DocGen;
using Xunit;

namespace BlazorCompose.Site.DocGen.Tests;

public class DocNameTests
{
    [Theory]
    [InlineData("getting-started", "GettingStarted")]
    [InlineData("intro", "Intro")]
    [InlineData("api_reference", "ApiReference")]
    [InlineData("multi word.name", "MultiWordName")]
    public void ToIdentifier_PascalCasesStem(string stem, string expected)
    {
        Assert.Equal(expected, DocName.ToIdentifier(stem));
    }

    [Fact]
    public void ToIdentifier_LeadingDigit_PrefixesUnderscore()
    {
        Assert.Equal("_2024Notes", DocName.ToIdentifier("2024-notes"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("---")]
    public void ToIdentifier_NoAlphanumeric_Throws(string stem)
    {
        Assert.Throws<ArgumentException>(() => DocName.ToIdentifier(stem));
    }
}

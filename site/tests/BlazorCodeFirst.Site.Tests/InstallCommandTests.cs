using Xunit;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>
/// The install command the site shows, held against the one the repository README publishes.
/// </summary>
/// <remarks>
/// This exists because the site got it wrong. The landing page's copy button and
/// <c>getting-started.md</c> both offered <c>dotnet add package BlazorCodeFirst</c> with no
/// <c>--prerelease</c>, while both READMEs had the flag and explained why it is needed. The
/// published version carries a prerelease suffix, so the site's command resolved nothing -- the
/// first command a reader runs, failing, with every other check on the site green.
///
/// The expectation is read out of README.md rather than written here, so the two cannot drift the
/// way they already did. README.md is the source because §Installation there is where the reason is
/// recorded; a literal in this file would be a third copy of the same string.
/// </remarks>
public class InstallCommandTests
{
    private static string Expected()
    {
        foreach (string line in File.ReadAllLines(RepositoryPath.From("README.md")))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("dotnet add package BlazorCodeFirst", StringComparison.Ordinal))
            {
                return trimmed;
            }
        }

        throw new InvalidOperationException(
            "README.md no longer carries a 'dotnet add package BlazorCodeFirst' line for this to read.");
    }

    [Fact]
    public void TheReadmeCommandCarriesThePrereleaseFlag() =>
        // Guards the guard: were the flag to fall out of README.md, every assertion below would
        // still pass and all three copies would be wrong together.
        Assert.EndsWith("--prerelease", Expected(), StringComparison.Ordinal);

    [Fact]
    public void TheLandingPageOffersTheSameCommand() =>
        Assert.Contains(
            Expected(),
            File.ReadAllText(RepositoryPath.From("site/BlazorCodeFirst.Site/Pages/Home.cs")),
            StringComparison.Ordinal);

    [Fact]
    public void GettingStartedShowsTheSameCommand() =>
        Assert.Contains(
            Expected(),
            File.ReadAllText(RepositoryPath.From("site/content/getting-started.md")),
            StringComparison.Ordinal);
}

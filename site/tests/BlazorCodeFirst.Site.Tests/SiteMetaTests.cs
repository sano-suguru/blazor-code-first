using System.Collections.Immutable;
using BlazorCodeFirst.Site.Layout;
using Xunit;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>
/// The head tags a route declares about itself.
/// </summary>
/// <remarks>
/// Tested as a decision over its inputs rather than through a rendered component, for the reason
/// #279 gives about the rest of this project: what can go wrong here is which tags a route gets and
/// what each one says, and that is a function, not a DOM.
/// </remarks>
public class SiteMetaTests
{
    private static ImmutableArray<HeadTag> Tags(
        string path = "/docs/getting-started/",
        string lang = "en",
        params DocAlternate[] alternates) =>
        SiteMeta.Tags("Getting Started", "How to start.", path, lang, alternates);

    /// <summary>
    /// The href or content of the one tag matching an element and an identifying attribute, or null.
    /// </summary>
    /// <remarks>
    /// Asserts uniqueness rather than taking the first match, because every claim below is about a
    /// tag a page declares once. A second copy would otherwise pass silently and leave the crawler
    /// to pick.
    /// </remarks>
    private static string? Value(ImmutableArray<HeadTag> tags, string element, string key, string name)
    {
        var matches = tags
            .Where(tag => string.Equals(tag.Element, element, StringComparison.Ordinal))
            .Where(tag => tag.Attributes.Any(a =>
                string.Equals(a.Key, key, StringComparison.Ordinal) &&
                string.Equals(a.Value, name, StringComparison.Ordinal)))
            .ToList();

        if (matches.Count == 0)
        {
            return null;
        }

        Assert.Single(matches);
        return matches[0].Attributes
            .First(a => a.Key is "href" or "content")
            .Value;
    }

    private static DocAlternate[] BothEditions() =>
    [
        new DocAlternate("en", "/docs/getting-started/"),
        new DocAlternate("ja", "/docs/ja/getting-started/"),
    ];

    [Fact]
    public void Tags_CanonicalIsTheOriginPlusThePath() =>
        Assert.Equal(
            SiteMetadata.Origin + "/docs/getting-started/",
            Value(Tags(), "link", "rel", "canonical"));

    [Fact]
    public void Tags_HomeCanonicalHasNoDoubledSlash() =>
        Assert.Equal(SiteMetadata.Origin + "/", Value(Tags(path: "/"), "link", "rel", "canonical"));

    [Fact]
    public void Tags_DeclaresTheDescriptionOnce() =>
        Assert.Equal("How to start.", Value(Tags(), "meta", "name", "description"));

    [Fact]
    public void Tags_OgUrlMatchesTheCanonical()
    {
        var tags = Tags();

        Assert.Equal(
            Value(tags, "link", "rel", "canonical"),
            Value(tags, "meta", "property", "og:url"));
    }

    [Fact]
    public void Tags_OgImageIsAbsolute() =>
        Assert.Equal(
            SiteMetadata.Origin + SiteMetadata.CardPath,
            Value(Tags(), "meta", "property", "og:image"));

    [Fact]
    public void Tags_CardCarriesTheTitleAndTheDescription()
    {
        var tags = Tags();

        Assert.Equal("Getting Started", Value(tags, "meta", "property", "og:title"));
        Assert.Equal("How to start.", Value(tags, "meta", "property", "og:description"));
        Assert.Equal("Getting Started", Value(tags, "meta", "name", "twitter:title"));
        Assert.Equal("How to start.", Value(tags, "meta", "name", "twitter:description"));
    }

    [Fact]
    public void Tags_NoAlternates_DeclaresNoHreflang() =>
        // An hreflang set has to be reciprocal, so a page nothing else names cannot be part of one.
        Assert.DoesNotContain(
            Tags(path: "/"),
            tag => tag.Attributes.Any(a => string.Equals(a.Key, "hreflang", StringComparison.Ordinal)));

    [Fact]
    public void Tags_Alternates_NameEveryEditionIncludingThisOne()
    {
        var tags = Tags(path: "/docs/ja/getting-started/", lang: "ja", BothEditions());

        Assert.Equal(
            SiteMetadata.Origin + "/docs/getting-started/",
            Value(tags, "link", "hreflang", "en"));
        Assert.Equal(
            SiteMetadata.Origin + "/docs/ja/getting-started/",
            Value(tags, "link", "hreflang", "ja"));
    }

    [Fact]
    public void Tags_Alternates_PointXDefaultAtTheCanonicalEdition() =>
        // The answer for a reader whose language this site has no edition for.
        Assert.Equal(
            SiteMetadata.Origin + "/docs/getting-started/",
            Value(Tags(path: "/docs/ja/getting-started/", lang: "ja", BothEditions()),
                "link", "hreflang", "x-default"));

    [Fact]
    public void Tags_UntranslatedDocument_NamesItselfAndNoXDefaultElsewhere()
    {
        var tags = Tags(
            path: "/docs/getting-started/",
            lang: "en",
            new DocAlternate("en", "/docs/getting-started/"));

        Assert.Equal(
            SiteMetadata.Origin + "/docs/getting-started/",
            Value(tags, "link", "hreflang", "en"));
        Assert.Null(Value(tags, "link", "hreflang", "ja"));
    }

    [Theory]
    [InlineData("en", "en_US")]
    [InlineData("ja", "ja_JP")]
    public void Tags_DeclareTheLocaleOfTheirOwnEdition(string lang, string locale) =>
        Assert.Equal(locale, Value(Tags(lang: lang), "meta", "property", "og:locale"));

    [Fact]
    public void Tags_EveryTagIsDeclaredExactlyOnce()
    {
        // The whole set at once, rather than the tags the assertions above happen to name. HeadOutlet
        // appends to the head without replacing what is there, so a duplicate would ship and leave
        // the crawler to choose between two answers.
        var identities = Tags(path: "/docs/ja/getting-started/", lang: "ja", BothEditions())
            .Select(tag => tag.Element + "|" + string.Join(
                ",",
                tag.Attributes
                    .Where(a => a.Key is not ("href" or "content"))
                    .Select(a => a.Key + "=" + a.Value)))
            .ToList();

        Assert.Equal(identities.Count, identities.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Tags_EveryUrlIsAbsolute() =>
        Assert.All(
            Tags(path: "/docs/ja/getting-started/", lang: "ja", BothEditions()),
            tag => Assert.All(
                tag.Attributes.Where(a => a.Key is "href" || a.Value.StartsWith('/')),
                a => Assert.StartsWith(SiteMetadata.Origin, a.Value, StringComparison.Ordinal)));
}

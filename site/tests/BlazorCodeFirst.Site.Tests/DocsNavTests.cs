using System.Collections.Immutable;
using BlazorCodeFirst.Site.Content;
using BlazorCodeFirst.Site.Layout;
using Xunit;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>Which editions the documentation rail offers a reader a switch to.</summary>
/// <remarks>
/// Here rather than in the browser suite because the case that matters most cannot be published.
/// Every English document in site/content is translated, and the edition's links are closed, so
/// removing a Japanese document fails the build on a dangling link rather than producing a document
/// with no counterpart. The browser assertion written for that case therefore never ran, and #279
/// moved it here, where the manifest holding an untranslated document is constructed directly.
///
/// What is asserted is the list the rail decides from, one layer beneath the element the reader
/// sees. The rendered side is the browser suite's, and it covers the case that can be published: a
/// document whose counterpart exists links to it.
/// </remarks>
public class DocsNavTests
{
    /// <summary>A manifest entry. Only the language and the slug are read by the decision under
    /// test, so the rest carries whatever keeps the record one DocGen could have emitted.</summary>
    private static DocEntry Doc(string lang, string slug, int order = 10) =>
        new(slug, slug, order, lang, false, "<p/>");

    /// <summary>Two English documents, one of which the Japanese edition has translated.</summary>
    private static readonly ImmutableArray<DocEntry> OneTranslatedOneNot =
        [Doc("en", "guide"), Doc("en", "extras", 20), Doc("ja", "guide")];

    [Fact]
    public void Counterparts_DocumentTheOtherEditionLacks_OffersNothing() =>
        Assert.Empty(DocsNav.Counterparts(OneTranslatedOneNot, "en", "extras"));

    [Fact]
    public void Counterparts_DocumentTheOtherEditionHas_OffersThatEdition() =>
        Assert.Equal(["ja"], DocsNav.Counterparts(OneTranslatedOneNot, "en", "guide"));

    [Fact]
    public void Counterparts_ReadFromTheTranslation_OffersTheCanonicalEdition() =>
        Assert.Equal(["en"], DocsNav.Counterparts(OneTranslatedOneNot, "ja", "guide"));

    [Fact]
    public void Counterparts_SlugDifferingOnlyInCase_OffersThatEdition() =>
        // Blazor route matching is case-insensitive, so /docs/Guide reaches the same document and
        // must be offered the same switch.
        Assert.Equal(["ja"], DocsNav.Counterparts(OneTranslatedOneNot, "en", "Guide"));

    [Fact]
    public void Counterparts_IndexRoute_OffersTheOtherEdition() =>
        // An index route names no document, so what it needs is only that the other edition exists.
        Assert.Equal(["ja"], DocsNav.Counterparts(OneTranslatedOneNot, "en", slug: null));
}

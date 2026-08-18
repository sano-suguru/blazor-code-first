using BlazorCodeFirst.Site.Content;
using BlazorCodeFirst.Site.Layout;
using Xunit;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>
/// Which documents offer a diagnostic-id filter, and which ids it offers.
/// </summary>
/// <remarks>
/// The decision is made from the manifest's anchors rather than from the slug, so the cases worth
/// holding are documents <c>site/content</c> does not have: one that publishes an id-shaped anchor
/// by accident, and one that publishes none.
/// </remarks>
public class AnchorFilterTests
{
    private static DocEntry Doc(params string[] anchors) =>
        new("d", "D", "A summary.", 10, "reference", "en", false, [.. anchors], "<p/>");

    [Fact]
    public void ADocumentWithNoIdShapedAnchor_OffersNothing() =>
        Assert.False(AnchorFilter.Applies(Doc("installation", "a-first-component")));

    [Fact]
    public void ADocumentWithIdShapedAnchors_Applies() =>
        Assert.True(AnchorFilter.Applies(Doc("your-class-and-its-getter", "bcf1001")));

    [Fact]
    public void IdsAreOfferedInReadingOrder() =>
        // Not sorted. The reference groups its ids by subject, and a reader scanning the chips is
        // looking at the same order as the page under them.
        Assert.Equal(
            ["bcf3016", "bcf1001"],
            [.. AnchorFilter.Ids(Doc("elements", "bcf3016", "classes", "bcf1001"))]);

    [Theory]
    [InlineData("bcf")]              // no digits
    [InlineData("bcf3016-and-more")] // an id plus something else
    [InlineData("bcf30a6")]          // a letter among the digits
    [InlineData("BCF3016")]          // anchors are lowercase; an uppercase one is not one of ours
    [InlineData("abcf3016")]         // does not start with the prefix
    public void AnAnchorThatMerelyResemblesAnId_IsNotOffered(string anchor) =>
        Assert.Empty(AnchorFilter.Ids(Doc(anchor)));

    [Fact]
    public void TheDiagnosticsReferenceOffersEveryIdItDocuments()
    {
        // Over the real manifest, in both editions: the chips must be the sections the page has, and
        // the two editions document the same set of diagnostics.
        var english = Docs.Find(Docs.Canonical, "diagnostics");
        var japanese = Docs.Find("ja", "diagnostics");
        Assert.NotNull(english);
        Assert.NotNull(japanese);

        Assert.True(AnchorFilter.Ids(english).Length > 40, "the reference documents more than 40 ids");

        // Materialized on both sides. ImmutableArray<T> implements IEquatable<ImmutableArray<T>> by
        // comparing the underlying array by reference, so Assert.Equal over two of them binds to the
        // equatable overload and reports "collections differ" for arrays that hold the same strings.
        Assert.Equal([.. AnchorFilter.Ids(english)], [.. AnchorFilter.Ids(japanese)]);
    }

    [Theory]
    [InlineData("{shown} of {total}", 12, 42, "12 of 42")]
    // A translation may put the total first, the way the real Japanese shell.yml does; FormatCount
    // only substitutes by name, so it does not care which placeholder was written first. Source here
    // stays English by rule, so the reordering is exercised with an ASCII stand-in.
    [InlineData("{total} total, {shown} shown", 12, 42, "42 total, 12 shown")]
    public void FormatCount_SubstitutesShownAndTotalByName(
        string template, int shown, int total, string expected) =>
        Assert.Equal(expected, AnchorFilter.FormatCount(template, shown, total));

    [Fact]
    public void NoOtherDocumentOffersOne() =>
        // The filter is a property of the content, not of a route. If a second document starts
        // publishing ids, this says so rather than the filter appearing somewhere unnoticed.
        Assert.Equal(
            ["diagnostics"],
            [.. Docs.All.Where(AnchorFilter.Applies).Select(d => d.Slug).Distinct()]);
}

using System.Collections.Immutable;
using BlazorCodeFirst.Site.Content;
using Xunit;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>
/// The grouping the rail and the index both read, asked over a manifest handed in rather than the
/// one this build produced.
/// </summary>
/// <remarks>
/// Same ground as <see cref="DocsNavTests"/> and the same reason (#279): the cases worth holding are
/// ones <c>site/content</c> cannot hold at once. A translation that has reached only one group is
/// the important one, because the rail must not print a heading with nothing under it, and content
/// that showed it would mean deleting documents from the Japanese edition to keep the test true.
/// </remarks>
public class DocsGroupingTests
{
    private static DocEntry Doc(string lang, string slug, int order, string group) =>
        new(slug, slug, "A summary.", order, group, lang, false, [], "<p/>");

    /// <summary>English across all three groups; Japanese has translated only the first.</summary>
    private static readonly ImmutableArray<DocEntry> Manifest =
    [
        Doc("en", "start-a", 10, "start"),
        Doc("en", "write-a", 20, "write"),
        Doc("en", "write-b", 30, "write"),
        Doc("en", "ref-a", 40, "reference"),
        Doc("ja", "start-a", 10, "start"),
    ];

    [Fact]
    public void GroupsFor_ListsGroupsInNavigationOrder() =>
        Assert.Equal(["start", "write", "reference"], [.. Docs.GroupsFor(Manifest, "en")]);

    [Fact]
    public void GroupsFor_LeavesOutAGroupThisLanguageHasNoDocumentIn() =>
        Assert.Equal(["start"], [.. Docs.GroupsFor(Manifest, "ja")]);

    [Fact]
    public void ForGroup_ReturnsOneGroupsDocumentsInReadingOrder() =>
        Assert.Equal(["write-a", "write-b"], Docs.ForGroup(Manifest, "en", "write").Select(d => d.Slug));

    [Fact]
    public void ForGroup_DoesNotReachAnotherLanguage() =>
        Assert.Empty(Docs.ForGroup(Manifest, "ja", "write"));

    /// <summary>
    /// Every group in the manifest reaches exactly one heading, so no rail can print a raw key.
    /// </summary>
    /// <remarks>
    /// GroupLabel falls back to the group name when the shell text has no entry for it, which is the
    /// one way an English word could reach a translated rail. Nothing in site/content triggers that
    /// today; this is what says so, rather than the fallback being trusted to stay unreachable.
    /// </remarks>
    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void GroupLabel_IsTranslatedForEveryGroup(string lang)
    {
        foreach (string group in Docs.GroupOrder)
        {
            Assert.NotEqual(group, Docs.GroupLabel(lang, group));
        }
    }
}

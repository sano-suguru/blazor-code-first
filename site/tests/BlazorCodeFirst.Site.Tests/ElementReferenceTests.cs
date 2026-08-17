using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>
/// The element list in <c>element-reference.md</c>, held against the helpers the runtime actually
/// declares.
/// </summary>
/// <remarks>
/// A reference page is read to find out whether something exists, so a name it omits reads as "there
/// is no helper for this" and a name it invents sends the reader to write code that will not
/// compile. Neither shows up in a build, a link check, or a rendered page.
///
/// The expectation is reflected off <c>Html</c> rather than listed here, which is what makes adding
/// an element to the runtime fail this test until the page has it too.
/// </remarks>
public partial class ElementReferenceTests
{
    [GeneratedRegex(@"`([A-Z][A-Za-z0-9]*)`")]
    private static partial Regex ListedName();

    private static string Page() => File.ReadAllText(RepositoryPath.From("site/content/element-reference.md"));

    /// <summary>Every <c>Html</c> property that stands for one HTML element.</summary>
    private static HashSet<string> Declared() =>
    [
        .. typeof(Html)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(ElementView))
            .Select(p => p.Name),
    ];

    /// <summary>The names the page lists under its element headings, before "Everything else".</summary>
    private static HashSet<string> Listed()
    {
        string page = Page();
        int start = page.IndexOf("### Sections", StringComparison.Ordinal);
        int end = page.IndexOf("### Everything else", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "element-reference.md no longer has the headings this reads between.");

        return [.. ListedName().Matches(page[start..end]).Select(m => m.Groups[1].Value)];
    }

    [Fact]
    public void EveryDeclaredElementIsListed()
    {
        var missing = Declared().Except(Listed()).Order(StringComparer.Ordinal);
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryListedElementIsDeclared()
    {
        var invented = Listed().Except(Declared()).Order(StringComparer.Ordinal);
        Assert.Empty(invented);
    }

    [Fact]
    public void TheCountThePageClaimsIsTheCountThereAre()
    {
        // The page opens with "There are 100 of them". A sentence carrying a number is the half a
        // reader trusts without checking, so it is checked here rather than re-counted by hand.
        var claimed = Regex.Match(Page(), @"There are (\d+) of them", RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.True(claimed.Success, "element-reference.md no longer states how many elements there are.");
        Assert.Equal(Declared().Count.ToString(System.Globalization.CultureInfo.InvariantCulture), claimed.Groups[1].Value);
    }
}

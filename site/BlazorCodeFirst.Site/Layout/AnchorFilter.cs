using System.Collections.Immutable;
using System.Globalization;
using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// A filter over the diagnostic ids a document publishes, above the document itself.
/// </summary>
/// <remarks>
/// The diagnostics reference is 42 sections on one page, and the only way to reach one was the
/// browser's own find. That works if you already know you are looking for "BCF3016" and not, say,
/// every id in the 30xx range; it does not answer "which of these is mine" at all.
///
/// The ids come from the manifest rather than from a list written here. DocGen carries every
/// document's heading anchors through, so this offers exactly the sections the document publishes:
/// renaming a heading moves the chip with it, and deleting one deletes the chip.
///
/// It renders on any document that publishes ids of this shape, which today is the diagnostics
/// reference in both editions. Nothing names that document, because a page that lists ids is a
/// property of the content rather than of the route.
///
/// Before WebAssembly starts, the input is inert and every chip is shown. That is the right resting
/// state: the chips are ordinary links to ids on the page, so the whole thing degrades to a table of
/// contents rather than to nothing.
/// </remarks>
public sealed partial class AnchorFilter : BodyComponentBase
{
    /// <summary>The document whose ids are offered.</summary>
    [Microsoft.AspNetCore.Components.Parameter]
    public DocEntry Entry { get; set; } = default!;

    private string _filter = "";

    /// <summary>Whether a document publishes ids this component can offer.</summary>
    public static bool Applies(DocEntry entry) => Ids(entry).Length > 0;

    /// <summary>The diagnostic ids the document publishes, in reading order.</summary>
    /// <remarks>
    /// An anchor is one when it is the whole id and nothing else -- "bcf3016" and not "bcf3016-and-
    /// friends" -- so a section that merely mentions one in its heading does not become a chip.
    /// </remarks>
    internal static ImmutableArray<string> Ids(DocEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (string anchor in entry.Anchors)
        {
            if (IsDiagnosticId(anchor))
            {
                builder.Add(anchor);
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsDiagnosticId(string anchor)
    {
        if (anchor.Length < 4 || !anchor.StartsWith("bcf", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in anchor.AsSpan(3))
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The ids left after what the reader typed.</summary>
    /// <remarks>
    /// Matched against the id as it is printed, so "3016", "BCF3016" and "bcf30" all narrow the same
    /// way. Nothing is matched against the section's prose: the reader who needs this one knows the
    /// id and not the wording, which is the whole reason the build prints the id.
    /// </remarks>
    private ImmutableArray<string> Matches(ImmutableArray<string> all)
    {
        if (_filter.Trim().Length == 0)
        {
            return all;
        }

        string needle = _filter.Trim();
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (string id in all)
        {
            if (id.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                builder.Add(id);
            }
        }

        return builder.ToImmutable();
    }

    protected override View Body
    {
        get
        {
            var all = Ids(Entry);
            var matches = Matches(all);
            var shell = Docs.Shell(Entry.Lang);
            return Div.Class("anchor-filter")[
                Label.Class("anchor-filter-label").Attr("for", InputId)[
                    shell.AnchorFilterLabel],
                Input
                    .Id(InputId)
                    .Type("search")
                    .Class("anchor-filter-input")
                    .Attr("autocomplete", "off")
                    .Bind("value", "oninput", () => _filter),
                Div.Class("visually-hidden").Attr("role", "status").Attr("aria-live", "polite")[
                    FormatCount(shell.AnchorFilterCount, matches.Length, all.Length)],
                Ul.Class("anchor-chips")[
                    ForEach(
                        matches,
                        key: id => id,
                        content: id => Li[
                            A.Href("#" + id).Class("anchor-chip")[id.ToUpperInvariant()]])]];
        }
    }

    /// <summary>Substitutes the diagnostics filter's live-region sentence, matching each named
    /// placeholder by name rather than by position.</summary>
    /// <remarks>
    /// A translation may need the total before the shown count, which is a difference in word order
    /// rather than in the two numbers themselves — DocGen's shell file reader checks that
    /// <c>shell.yml</c> carries both names and no others, not that they appear in a fixed order.
    /// </remarks>
    internal static string FormatCount(string template, int shown, int total) =>
        template
            .Replace("{shown}", shown.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{total}", total.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private const string InputId = "anchor-filter";
}

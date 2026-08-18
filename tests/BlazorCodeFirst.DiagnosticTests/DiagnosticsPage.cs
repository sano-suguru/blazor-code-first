using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>One entry on the documentation site's diagnostic reference.</summary>
/// <param name="Kind">
/// The severity the entry claims, or <see langword="null"/> when it claims none. Kept as the
/// document's own word for the reason <see cref="AppendixARow"/> keeps 種別: a value the page invents
/// is reported instead of being silently mapped onto the nearest real severity.
/// </param>
internal sealed record DiagnosticsPageEntry(string Id, string? Kind);

/// <summary>
/// What <c>site/content/diagnostics.md</c> documents, read out of the page itself.
/// </summary>
/// <remarks>
/// <para>
/// The site-side twin of <see cref="AppendixA"/>, and deliberately a second reader rather than a
/// second call into that one. The two documents answer different readers and have different shapes:
/// Appendix A is one table, this is one heading per diagnostic so a document can link a reader straight to
/// the entry that names their build failure. A shared parser would tie the page's layout to the
/// appendix's, and neither is free to change without the other.
/// </para>
/// <para>
/// The heading carries the ID and nothing else, because it is also the anchor. Markdig derives the
/// anchor from the heading text, so <c>### BCF3016 — Error</c> would publish <c>#bcf3016--error</c>
/// and every link into this page would carry a severity that a later change could move. The severity
/// is the first word of the entry instead.
/// </para>
/// <para>
/// Tolerant about everything else. Prose, examples, and the order of the entries can all change
/// without touching this.
/// </para>
/// </remarks>
internal static partial class DiagnosticsPage
{
    public static string DocumentPath { get; } =
        Path.Combine(RepoLayout.Root, "site", "content", "diagnostics.md");

    /// <summary>
    /// Every entry in document order, keeping duplicates so a repeated diagnostic stays visible
    /// rather than collapsing into a set comparison that passes.
    /// </summary>
    public static ImmutableArray<DiagnosticsPageEntry> Entries { get; } = Parse();

    public static ImmutableArray<string> DocumentedIds { get; } =
        [.. Entries.Select(static entry => entry.Id)];

    private static ImmutableArray<DiagnosticsPageEntry> Parse()
    {
        if (!File.Exists(DocumentPath))
        {
            throw new InvalidOperationException(
                $"{DocumentPath} does not exist. The site's diagnostic reference was moved or renamed; " +
                "point this parser at wherever it lives now.");
        }

        var lines = File.ReadAllLines(DocumentPath);
        var entries = ImmutableArray.CreateBuilder<DiagnosticsPageEntry>();

        for (var i = 0; i < lines.Length; i++)
        {
            var heading = EntryHeading().Match(lines[i]);
            if (!heading.Success)
            {
                continue;
            }

            entries.Add(new DiagnosticsPageEntry(heading.Groups["id"].Value, SeverityAfter(lines, i)));
        }

        return entries.ToImmutable();
    }

    /// <summary>
    /// The severity word opening the first paragraph under the heading at <paramref name="index"/>,
    /// or <see langword="null"/> when that paragraph does not open with one.
    /// </summary>
    /// <remarks>
    /// Stops at the next heading so an entry with no body reads as claiming nothing rather than
    /// borrowing the next entry's severity, which would let a deleted paragraph pass.
    /// </remarks>
    private static string? SeverityAfter(string[] lines, int index)
    {
        for (var i = index + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith('#'))
            {
                return null;
            }

            if (lines[i].Trim().Length == 0)
            {
                continue;
            }

            var opener = SeverityOpener().Match(lines[i]);
            return opener.Success ? opener.Groups["kind"].Value : null;
        }

        return null;
    }

    [GeneratedRegex(@"^###\s+(?<id>BCF\d{4})\s*$")]
    private static partial Regex EntryHeading();

    [GeneratedRegex(@"^(?<kind>Error|Warning|Info)\.\s")]
    private static partial Regex SeverityOpener();
}

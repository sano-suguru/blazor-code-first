using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace BlazorCompose.DiagnosticTests;

/// <summary>One row of the 付録A table: the diagnostic it documents and the severity it claims.</summary>
/// <param name="Kind">
/// The 種別 cell, or <see langword="null"/> when the row states none.  Kept as the document's own word
/// rather than a <c>DiagnosticSeverity</c> so a value the table invents is reported instead of being
/// silently mapped onto the nearest real severity.
/// </param>
internal sealed record AppendixARow(string Id, string? Kind);

/// <summary>
/// What <c>ARCHITECTURE.md</c> 付録A documents, read out of the canonical table itself.
/// </summary>
/// <remarks>
/// Deliberately tolerant about everything except what is being checked: it takes the section between
/// the 付録A heading and the next top-level heading, then every row whose first cell is a BCF ID and
/// reads the second cell as the severity.  The prose, the column count beyond the second, and the
/// sub-heading structure inside 付録A can all change without touching this parser.  Moving or renaming
/// the section fails loudly here; reordering the columns fails in
/// <c>DiagnosticTableTests</c> instead of passing quietly.
/// </remarks>
internal static partial class AppendixA
{
    private const string SectionHeading = "## 付録A";
    private const string TopLevelHeading = "## ";

    public static string DocumentPath { get; } = Path.Combine(RepoLayout.Root, "ARCHITECTURE.md");

    /// <summary>
    /// Every documented row in table order, keeping duplicates so a repeated diagnostic is visible
    /// rather than collapsing into a set comparison that passes.
    /// </summary>
    public static ImmutableArray<AppendixARow> Rows { get; } = Parse();

    public static ImmutableArray<string> DocumentedIds { get; } = [.. Rows.Select(static row => row.Id)];

    private static ImmutableArray<AppendixARow> Parse()
    {
        var lines = File.ReadAllLines(DocumentPath);
        var start = Array.FindIndex(lines, static line => line.StartsWith(SectionHeading, StringComparison.Ordinal));
        if (start < 0)
        {
            throw new InvalidOperationException(
                $"'{SectionHeading}' is not in {DocumentPath}. The canonical diagnostic table was moved or " +
                "renamed; point this parser at wherever it lives now.");
        }

        var rows = ImmutableArray.CreateBuilder<AppendixARow>();
        for (var index = start + 1;
             index < lines.Length && !lines[index].StartsWith(TopLevelHeading, StringComparison.Ordinal);
             index++)
        {
            if (Row().Match(lines[index]) is not { Success: true } match)
                continue;

            var kind = match.Groups["kind"].Value.Trim();
            rows.Add(new AppendixARow(match.Groups["id"].Value, kind.Length > 0 ? kind : null));
        }

        return rows.ToImmutable();
    }

    [GeneratedRegex(@"^\|\s*`?(?<id>BCF\d{4})`?\s*\|(?<kind>[^|]*)", RegexOptions.ExplicitCapture)]
    private static partial Regex Row();
}

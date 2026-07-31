using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace BlazorCompose.DiagnosticTests;

/// <summary>
/// The diagnostic IDs <c>ARCHITECTURE.md</c> 付録A documents, read out of the canonical table itself.
/// </summary>
/// <remarks>
/// Deliberately tolerant about everything except the one thing being checked: it takes the section
/// between the 付録A heading and the next top-level heading, then every row whose first cell is a BC
/// ID. The prose, the column count, and the sub-heading structure inside 付録A can all change without
/// touching this parser — only moving or renaming the section can, and that fails loudly.
/// </remarks>
internal static partial class AppendixA
{
    private const string SectionHeading = "## 付録A";
    private const string TopLevelHeading = "## ";

    public static string DocumentPath { get; } = Path.Combine(RepoLayout.Root, "ARCHITECTURE.md");

    /// <summary>
    /// Every documented ID in table order, keeping duplicates so a repeated row is visible rather than
    /// collapsing into a set comparison that passes.
    /// </summary>
    public static ImmutableArray<string> DocumentedIds { get; } = Parse();

    private static ImmutableArray<string> Parse()
    {
        var lines = File.ReadAllLines(DocumentPath);
        var start = Array.FindIndex(lines, static line => line.StartsWith(SectionHeading, StringComparison.Ordinal));
        if (start < 0)
        {
            throw new InvalidOperationException(
                $"'{SectionHeading}' is not in {DocumentPath}. The canonical diagnostic table was moved or " +
                "renamed; point this parser at wherever it lives now.");
        }

        var ids = ImmutableArray.CreateBuilder<string>();
        for (var index = start + 1;
             index < lines.Length && !lines[index].StartsWith(TopLevelHeading, StringComparison.Ordinal);
             index++)
        {
            if (RowId().Match(lines[index]) is { Success: true } match)
                ids.Add(match.Groups["id"].Value);
        }

        return ids.ToImmutable();
    }

    [GeneratedRegex(@"^\|\s*`?(?<id>BC\d{4})`?\s*\|", RegexOptions.ExplicitCapture)]
    private static partial Regex RowId();
}

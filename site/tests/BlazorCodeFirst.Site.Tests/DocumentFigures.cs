using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Site.Tests;

/// <summary>One output figure a document carries: the pair, and the component it names.</summary>
/// <param name="Document">The document's file name, so a failure says which one to open.</param>
/// <param name="Name">
/// The example type's name, unqualified. The marker names it rather than the test listing it, so a
/// figure added to a document is checked from the commit that adds it.
/// </param>
internal sealed record DocumentFigure(string Document, string Name, string CSharp, string Html);

/// <summary>
/// Every figure pair the documents under <c>site/content</c> carry.
/// </summary>
/// <remarks>
/// <para>
/// A pair is an HTML comment naming the example, then a <c>csharp</c> fence, then an <c>html</c>
/// fence. Markdig passes the comment through untouched, so no DocGen change is needed to carry the
/// name from the document to the test: the marker is invisible to a reader and load-bearing to the
/// check.
/// </para>
/// <para>
/// Derived from the documents rather than from a list here. A list would have to be edited twice for
/// every figure, and the edit nobody makes is the one that silently drops a figure out of the run --
/// the shape of defect #163 records.
/// </para>
/// </remarks>
internal static partial class DocumentFigures
{
    public static string ContentDirectory { get; } =
        RepositoryPath.From(Path.Combine("site", "content"));

    public static ImmutableArray<DocumentFigure> All { get; } = Read();

    private static ImmutableArray<DocumentFigure> Read()
    {
        var figures = ImmutableArray.CreateBuilder<DocumentFigure>();

        // Top-level files only. A subdirectory of content/ is a translation, and a translation shows
        // the same figure as the document it follows: the markup a C# expression renders carries no
        // language, so a second copy would be a second thing to keep in step for no reader's benefit.
        foreach (var path in Directory.EnumerateFiles(ContentDirectory, "*.md").Order(StringComparer.Ordinal))
        {
            var document = Path.GetFileName(path);
            foreach (Match match in Marker().Matches(File.ReadAllText(path)))
            {
                figures.Add(new DocumentFigure(
                    document,
                    match.Groups["name"].Value,
                    match.Groups["cs"].Value.ReplaceLineEndings("\n").TrimEnd(),
                    match.Groups["html"].Value.ReplaceLineEndings("\n").TrimEnd()));
            }
        }

        return figures.ToImmutable();
    }

    /// <summary>
    /// The marker and the two fences that must follow it, in that order and with nothing else
    /// between them.
    /// </summary>
    /// <remarks>
    /// Anchored on the marker rather than on the fences, so a pair whose html half was deleted does
    /// not match at all. That reads as a missing figure in the count assertion, which names the
    /// document, rather than as a silently unchecked csharp fence.
    /// </remarks>
    [GeneratedRegex(
        """<!--\s*bcf-figure:\s*(?<name>[A-Za-z0-9]+)\s*-->\s*```csharp\r?\n(?<cs>.*?)\r?\n```\s*```html\r?\n(?<html>.*?)\r?\n```""",
        RegexOptions.Singleline)]
    private static partial Regex Marker();
}

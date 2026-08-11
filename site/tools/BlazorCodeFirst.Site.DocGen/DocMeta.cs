using System.Text.RegularExpressions;
using Markdig.Syntax;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>One document's routing and navigation metadata.</summary>
/// <param name="Lang">The language tag from <see cref="DocLang"/>, taken from the directory.</param>
/// <param name="Stale">
/// True when a translation's <c>source-hash</c> does not match its English counterpart. Always false
/// for a canonical document, which has nothing to fall behind.
/// </param>
public sealed record DocMeta(string Slug, string Title, int Order, string Lang, bool Stale);

/// <summary>The languages the content directory may hold, and where each one's documents route to.</summary>
/// <remarks>
/// Language is derived from the directory rather than declared in front matter, so a file cannot
/// claim a language its neighbours do not have. That also gives a subdirectory a meaning: before
/// this, a file in one was silently dropped, and now every directory is either a language or an
/// error.
/// </remarks>
public static class DocLang
{
    /// <summary>The canonical language. Its documents are the top-level files.</summary>
    public const string Canonical = "en";

    /// <summary>Every language, canonical first.</summary>
    public static readonly string[] All = [Canonical, "ja"];

    /// <summary>The subdirectory a language's documents live in, or null for the canonical one.</summary>
    public static string? Directory(string lang) => lang == Canonical ? null : lang;

    /// <summary>The route a language's documents are served from, with no trailing slash.</summary>
    public static string RoutePrefix(string lang) => lang == Canonical ? "/docs" : $"/docs/{lang}";
}

/// <summary>Validates that a content file name can be used verbatim as a URL slug.</summary>
/// <remarks>
/// The slug becomes the <c>/docs/{slug}</c> route segment, so it must be lowercase, URL-safe, and
/// free of leading, trailing, or doubled separators. This replaces the identifier validation that the
/// removed <c>DocName</c> type performed when each document produced a C# member name.
/// </remarks>
public static partial class DocSlug
{
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex SlugPattern();

    public static string Validate(string fileNameStem, string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileNameStem);
        ArgumentNullException.ThrowIfNull(fileName);

        if (!SlugPattern().IsMatch(fileNameStem))
        {
            throw new InvalidOperationException(
                $"Invalid document '{fileName}': the file name must be a URL-safe slug matching " +
                @"'^[a-z0-9]+(-[a-z0-9]+)*\z' (lowercase words separated by single hyphens), " +
                $"but was '{fileNameStem}'.");
        }

        return fileNameStem;
    }
}

/// <summary>Enforces the authoring rules a document body must follow.</summary>
public static class MarkdownBodyRules
{
    /// <summary>
    /// Rejects a top-level (h1) heading in the body. The front matter <c>title</c> is the single
    /// source of truth for a document's title and the page renders it as the h1 itself; allowing a
    /// body h1 would let the two drift apart permanently.
    /// </summary>
    /// <remarks>
    /// The check runs against the parsed document rather than the raw text so that it matches exactly
    /// what the renderer will produce. A hand-rolled line scan mistakes indented code blocks, a '='
    /// line following a list item or an ATX heading, and '~~~' inside a fenced block for headings,
    /// while missing tab-separated ATX headings and h1s nested in a blockquote or list item.
    /// </remarks>
    public static void EnsureNoTopLevelHeading(MarkdownDocument document, string fileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fileName);

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            if (heading.Level == 1)
            {
                throw new InvalidOperationException(
                    $"Invalid document '{fileName}': the body must not contain a top-level (h1) heading. " +
                    "The front matter 'title' is the single source of truth for the page title and is " +
                    "rendered as the h1 by the page itself; start the body at h2 ('## ').");
            }
        }
    }
}

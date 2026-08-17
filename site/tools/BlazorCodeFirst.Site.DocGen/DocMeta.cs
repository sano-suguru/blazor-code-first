using System.Text.RegularExpressions;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>One document's routing and navigation metadata.</summary>
/// <param name="Description">
/// The sentence a search result shows under the title, declared in front matter and bounded by
/// <see cref="DocDescription"/>.
/// </param>
/// <param name="Group">The navigation group from <see cref="DocGroup"/>, declared in front matter.</param>
/// <param name="Lang">The language tag from <see cref="DocLang"/>, taken from the directory.</param>
/// <param name="Stale">
/// True when a translation's <c>source-hash</c> does not match its English counterpart. Always false
/// for a canonical document, which has nothing to fall behind.
/// </param>
/// <param name="Anchors">
/// The ids this document publishes, in document order. Known only after parsing, so pass 1 builds
/// the record without them and pass 2 fills them in.
/// </param>
public sealed record DocMeta(
    string Slug,
    string Title,
    string Description,
    int Order,
    string Group,
    string Lang,
    bool Stale,
    IReadOnlyList<string> Anchors);

/// <summary>The navigation groups a document may declare, in the order the rail shows them.</summary>
/// <remarks>
/// Three, because a reader arrives with one of three questions: how do I start, how do I write this,
/// and what is the exact rule. A flat list answers none of them — it asks the reader to guess which
/// of eleven documents is the one that takes them from nothing to a rendered page.
///
/// The vocabulary is closed. An open one would let each document invent its own heading, which is
/// how a navigation ends up with as many groups as it has entries.
/// </remarks>
public static class DocGroup
{
    /// <summary>Getting from nothing to a component that renders.</summary>
    public const string Start = "start";

    /// <summary>The surface, feature by feature.</summary>
    public const string Write = "write";

    /// <summary>What is looked up rather than read: the vocabulary, the diagnostics, the FAQ.</summary>
    public const string Reference = "reference";

    /// <summary>Every group, in the order the navigation shows them.</summary>
    public static readonly string[] All = [Start, Write, Reference];

    /// <summary>Where a group sorts in the navigation.</summary>
    public static int Rank(string group) => Array.IndexOf(All, group);

    public static string Validate(string value, string fileName)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(fileName);

        if (Rank(value) < 0)
        {
            throw KeyValueBlock.Invalid(
                fileName,
                "document",
                $"front matter 'group' must be one of [{string.Join(", ", All)}] but was '{value}'.");
        }

        return value;
    }
}

/// <summary>Bounds how long a document's front matter <c>description</c> may run.</summary>
/// <remarks>
/// The description is what a search result shows under the title, and the result truncates it by
/// pixel width rather than by character count. A full-width character occupies about twice the width
/// of a Latin one, so one number applied to both editions would either cut the English short or let
/// the Japanese run past the edge. <see cref="SentenceLength"/> declines to measure Japanese at all
/// for a related reason; here the measurement is meaningful in both, and only the bound differs.
///
/// The limits sit above where the editions already are, so satisfying them costs no rewrite. What
/// they catch is a paragraph pasted into a field that holds a sentence.
/// </remarks>
public static class DocDescription
{
    /// <summary>How many characters a description may run to in the canonical edition.</summary>
    public const int CanonicalLimit = 160;

    /// <summary>How many characters a description may run to in a translation.</summary>
    public const int TranslationLimit = 90;

    /// <summary>The limit that applies to one language.</summary>
    /// <remarks>
    /// Takes the language rather than "is this the canonical edition", although those are the same
    /// question while there is one translation. The day a second one wants its own width budget, the
    /// answer changes here and at no call site.
    /// </remarks>
    public static int LimitFor(string lang) =>
        lang == DocLang.Canonical ? CanonicalLimit : TranslationLimit;

    /// <summary>Bounds a document's own description.</summary>
    public static string Validate(string value, string lang, string fileName) =>
        Validate(value, lang, fileName, "description", "document");

    /// <summary>Bounds any of the descriptions, whichever file and key carries it.</summary>
    public static string Validate(
        string value,
        string lang,
        string fileName,
        string key,
        string kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(lang);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(kind);

        int limit = LimitFor(lang);
        if (value.Length > limit)
        {
            throw KeyValueBlock.Invalid(
                fileName,
                kind,
                $"'{key}' runs to {value.Length} characters, and the limit is {limit}. " +
                "It is one sentence, not a paragraph.");
        }

        return value;
    }
}

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

    /// <summary>
    /// Rejects every custom container except <c>:::warning</c>. The site has one kind of warning and
    /// no other container.
    /// </summary>
    /// <remarks>
    /// The container extension takes any info string, so <c>:::note</c> parses and renders a
    /// <c>&lt;div class="note"&gt;</c> that no stylesheet paints — prose in a wrapper, indistinguishable
    /// from the paragraphs around it. The rule keeps the block a reader learns to stop at meaning one
    /// thing. Widening the set is a design change: <c>site/README.md</c> §Warnings carries the reason
    /// the set is closed, and the class has to be painted in both palettes before a second one exists.
    /// </remarks>
    /// <summary>The one container info string a document may open, and the class it renders as.
    /// <c>css/app.css</c> paints <c>.prose .warning</c>, which <c>StylesheetTests</c> holds.</summary>
    public const string WarningContainer = "warning";

    public static void EnsureOnlyWarningContainers(MarkdownDocument document, string fileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fileName);

        foreach (var container in document.Descendants<CustomContainer>())
        {
            // Ordinal, so ':::WARNING' is rejected rather than silently emitting a class that differs
            // in case from the one css/app.css paints.
            if (!string.Equals(container.Info, WarningContainer, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Invalid document '{fileName}': ':::{container.Info}' is not a container this site " +
                    "has. ':::warning' is the only one, and it is for content that can produce a " +
                    "security defect in a reader's own page. Write anything else as prose.");
            }
        }
    }
}

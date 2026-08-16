using System.Text.Unicode; // UnicodeRange / UnicodeRanges, the named blocks IsCjk is built from
using Markdig.Renderers.Html; // GetAttributes / AddClass / AddProperty extension methods
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>
/// Build-time transformations applied to a parsed document before it is rendered to HTML.
/// </summary>
/// <remarks>
/// Rewriting links at build time (rather than intercepting clicks at runtime) keeps the prerendered
/// HTML correct: the static route is baked in, so the links work with no JavaScript and cost nothing
/// at runtime.
/// </remarks>
public static class AstRewriter
{
    private const string MarkdownExtension = ".md";

    /// <summary>
    /// Rewrites links to sibling documents ("other.md", "./other.md#frag") into their SPA routes
    /// ("/docs/other", "/docs/other#frag"), and fails the build on a target that cannot resolve.
    /// </summary>
    /// <remarks>
    /// Only <see cref="LinkInline"/> nodes are considered. A raw HTML anchor written directly in the
    /// Markdown body (<c>&lt;a href="other.md"&gt;</c>) parses as HtmlInline/HtmlBlock and passes
    /// through untouched, neither rewritten nor checked, so document bodies must use Markdown link
    /// syntax. Autolinks are a different node type and are likewise out of scope.
    /// <para>
    /// <paramref name="knownSlugs"/> and <paramref name="routePrefix"/> both belong to the linking
    /// document's own language. A sibling link stays inside that language, which is what makes it a
    /// sibling: a reader following one from a translated page should land on the translation of the
    /// target, and a translation that has not been written yet is a missing target rather than a
    /// silent hop back into English.
    /// </para>
    /// </remarks>
    public static void RewriteRelativeLinks(
        MarkdownDocument document,
        IReadOnlySet<string> knownSlugs,
        string fileName,
        string routePrefix)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(knownSlugs);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(routePrefix);

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage || link.Url is null)
            {
                continue;
            }

            // Split the fragment BEFORE any classification: the scheme test below must not mistake a
            // colon inside a fragment ("other.md#step:1") for a URL scheme. The query-string check
            // already ran on the stripped path; both checks now agree on what they inspect.
            string path = link.Url;
            string fragment = "";
            int hash = path.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                fragment = path[hash..];
                path = path[..hash];
            }

            if (!IsSiblingFileTarget(path))
            {
                continue;
            }

            if (path.Contains('?', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Invalid document '{fileName}': the link '{link.Url}' carries a query string. " +
                    "Links between documents must be plain sibling references such as './other.md' " +
                    "or './other.md#section'.");
            }

            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path[2..];
            }

            if (!path.EndsWith(MarkdownExtension, StringComparison.Ordinal))
            {
                // The comparison above is Ordinal, so a differently-cased extension lands here.
                if (path.EndsWith(MarkdownExtension, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Invalid document '{fileName}': the link '{link.Url}' must be lowercase. " +
                        "Document file names and their '.md' extension are lowercase by rule.");
                }

                continue;
            }

            string stem = path[..^MarkdownExtension.Length];
            if (stem.Length == 0)
            {
                continue;
            }

            if (!knownSlugs.Contains(stem))
            {
                throw new InvalidOperationException(
                    $"Invalid document '{fileName}': the link '{link.Url}' points at a document that " +
                    "does not exist in the content directory.");
            }

            link.Url = $"{routePrefix}/{stem}{fragment}";
        }
    }

    /// <summary>Recognizes a same-directory file reference, rejecting rooted paths, absolute URLs,
    /// scheme-like targets ("mailto:", "tel:"), and any path with a directory part.</summary>
    /// <remarks>
    /// Takes the URL with its fragment already removed, so a colon inside a fragment cannot be read as
    /// a scheme. A pure fragment ("#section") therefore arrives here as an empty string and is
    /// rejected by the length guard.
    /// </remarks>
    private static bool IsSiblingFileTarget(string url)
    {
        if (url.Length == 0 || url[0] is '/')
        {
            return false;
        }

        int colon = url.IndexOf(':', StringComparison.Ordinal);
        int slash = url.IndexOf('/', StringComparison.Ordinal);

        // A colon before the first slash (or with no slash at all) means the target names a scheme.
        if (colon >= 0 && (slash < 0 || colon < slash))
        {
            return false;
        }

        // "./name.md" is the only accepted directory-ish prefix; "../x" and "sub/x" are not siblings.
        string withoutPrefix = url.StartsWith("./", StringComparison.Ordinal) ? url[2..] : url;
        return !withoutPrefix.Contains('/', StringComparison.Ordinal);
    }

    /// <summary>
    /// Appends a clickable anchor ("headlink") to every h2-h6 heading that has an id, so each section
    /// can be linked directly.
    /// </summary>
    /// <remarks>
    /// h1 is excluded: a document body must not contain one (the front matter title is rendered as the
    /// page h1 by the page itself). Heading ids are already assigned at parse time by
    /// UseAutoIdentifiers, so they can be read straight off the heading's attributes. The anchor
    /// carries a literal "#" as link text plus an aria-label rather than relying on CSS generated
    /// content, because an anchor with no text has no accessible name. Must be called at most once
    /// per parsed document: it appends an anchor rather than replacing one, so a second call would
    /// duplicate the anchor on every eligible heading.
    /// </remarks>
    public static void AddHeadingLinks(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            if (heading.Level < 2 || heading.Inline is null)
            {
                continue;
            }

            string? id = heading.GetAttributes().Id;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var anchor = new LinkInline { Url = "#" + id };
            anchor.GetAttributes().AddClass("headlink");
            anchor.GetAttributes().AddProperty("aria-label", "Permalink to this section");
            anchor.AppendChild(new LiteralInline("#"));
            heading.Inline.AppendChild(anchor);
        }
    }

    /// <summary>
    /// Drops a soft line break that sits between two CJK characters, so a wrapped Japanese paragraph
    /// reaches the reader as the sentence it was written as.
    /// </summary>
    /// <remarks>
    /// A soft break renders as "\n" and a browser renders that as a space. Between two Latin words
    /// the space was wanted anyway; between two Japanese characters it is a gap where the glyphs
    /// should touch. Removing the node rather than rendering it away keeps the decision in the
    /// document, where <see cref="MarkdownConverter"/>'s tests can read it off the HTML.
    /// <para>
    /// The join is deliberately narrow. A hard break is a &lt;br&gt; the author asked for and is left
    /// alone. A neighbour that is not text the reader sees as CJK — a code span, raw HTML, an image —
    /// keeps its space, because the Japanese edition sets a space around an inline <c>&lt;code&gt;</c>
    /// on purpose. A character outside the Basic Multilingual Plane arrives as a surrogate half,
    /// matches no range, and so also keeps its space.
    /// </para>
    /// </remarks>
    public static void JoinCjkSoftLineBreaks(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Materialize before mutating. Removing a node mid-walk happens to be safe in Markdig 0.41.3
        // — Descendants pushes a container's whole child list onto its own stack before yielding any
        // of it, so unlinking one child cannot strand the rest — but that is an implementation
        // detail rather than a documented guarantee. A walk that instead followed NextSibling from
        // the live tree would stop at the first join in a paragraph and silently leave the rest.
        foreach (var lineBreak in document.Descendants<LineBreakInline>().ToList())
        {
            if (lineBreak.IsHard)
            {
                continue;
            }

            if (LastCharacterOf(lineBreak.PreviousSibling) is { } before &&
                FirstCharacterOf(lineBreak.NextSibling) is { } after &&
                IsCjk(before) && IsCjk(after))
            {
                lineBreak.Remove();
            }
        }
    }

    /// <summary>The last character a reader sees in <paramref name="inline"/>, or null when the node
    /// renders as something other than text.</summary>
    /// <remarks>
    /// The boundary the join tests is the rendered one, so any container is descended into: a
    /// Japanese sentence that wraps onto a link whose own text is Japanese breaks between two
    /// Japanese characters, even though a link node sits between them. Matching
    /// <see cref="ContainerInline"/> rather than naming the two subtypes this pipeline produces
    /// today means an extension that adds a third needs no edit here. An image is the one container
    /// left out: it renders as an <c>&lt;img&gt;</c>, so its children are alt text rather than text
    /// beside the break. A code span is a leaf and is not descended into by rule.
    /// </remarks>
    private static char? LastCharacterOf(Inline? inline) => inline switch
    {
        LiteralInline literal =>
            literal.Content.IsEmpty ? null : literal.Content[literal.Content.End],
        LinkInline { IsImage: true } => null,
        ContainerInline container => LastCharacterOf(container.LastChild),
        _ => null,
    };

    /// <summary>The first character a reader sees in <paramref name="inline"/>, or null when the node
    /// renders as something other than text. Mirrors <see cref="LastCharacterOf"/>.</summary>
    private static char? FirstCharacterOf(Inline? inline) => inline switch
    {
        LiteralInline literal =>
            literal.Content.IsEmpty ? null : literal.Content[literal.Content.Start],
        LinkInline { IsImage: true } => null,
        ContainerInline container => FirstCharacterOf(container.FirstChild),
        _ => null,
    };

    /// <summary>Recognizes a character that Japanese sets without a word space beside it.</summary>
    /// <remarks>
    /// Named blocks rather than hex bounds, so no boundary here is a transcription anyone has to
    /// check. The one written out is the one carrying a decision: the fullwidth range stops at
    /// U+FF9F, the end of the halfwidth katakana block, where <c>HalfwidthandFullwidthForms</c> runs
    /// on to U+FFEF. U+FFA0 onward is halfwidth hangul filler and halfwidth symbols, which nothing
    /// in this edition sets and which have no claim to being joined.
    /// </remarks>
    private static bool IsCjk(char c) =>
        Within(c, UnicodeRanges.CjkSymbolsandPunctuation)
        || Within(c, UnicodeRanges.Hiragana)
        || Within(c, UnicodeRanges.Katakana)
        || Within(c, UnicodeRanges.CjkUnifiedIdeographsExtensionA)
        || Within(c, UnicodeRanges.CjkUnifiedIdeographs)
        || Within(c, UnicodeRanges.CjkCompatibilityIdeographs)
        || c is >= '\uFF01' and <= '\uFF9F';

    private static bool Within(char c, UnicodeRange range) =>
        c >= range.FirstCodePoint && c < range.FirstCodePoint + range.Length;
}

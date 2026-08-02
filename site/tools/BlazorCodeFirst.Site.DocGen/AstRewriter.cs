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
    /// through untouched — neither rewritten nor checked — so document bodies must use Markdown link
    /// syntax. Autolinks are a different node type and are likewise out of scope.
    /// </remarks>
    public static void RewriteRelativeLinks(
        MarkdownDocument document,
        IReadOnlySet<string> knownSlugs,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(knownSlugs);
        ArgumentNullException.ThrowIfNull(fileName);

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

            link.Url = $"/docs/{stem}{fragment}";
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
}

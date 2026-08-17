using Markdig.Renderers.Html; // GetAttributes extension method
using Markdig.Syntax;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>The set of ids a parsed document publishes, which is what a link's fragment names.</summary>
/// <remarks>
/// Headings are the whole set. UseAutoIdentifiers puts an id on every heading at parse time and
/// nothing else in this pipeline emits one: the body carries no raw HTML with an <c>id</c>, and the
/// generic-attributes extension that would let an author write <c>{#custom}</c> is not registered.
/// <para>
/// Read after parsing and before <see cref="AstRewriter.AddHeadingLinks"/>, which is the order
/// <see cref="MarkdownConverter"/> runs them in. The permalink that pass appends is a link to an id,
/// not a new one, so reading either side of it would give the same answer -- but the anchors of every
/// document have to be in hand before any document's links are checked, so the read happens first by
/// necessity.
/// </para>
/// </remarks>
public static class HeadingAnchors
{
    public static IReadOnlySet<string> Of(MarkdownDocument document) =>
        new HashSet<string>(InOrder(document), StringComparer.Ordinal);

    /// <summary>The same ids in document order, which is the order a reader meets them.</summary>
    /// <remarks>
    /// Carried into the manifest so a page can offer its own document's sections without a second,
    /// hand-written list of them. The diagnostics index is what needs it: 42 ids that must stay the
    /// 42 headings the document actually publishes.
    /// </remarks>
    public static IReadOnlyList<string> InOrder(MarkdownDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var anchors = new List<string>();
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            string? id = heading.GetAttributes().Id;
            if (!string.IsNullOrEmpty(id))
            {
                anchors.Add(id);
            }
        }

        return anchors;
    }
}

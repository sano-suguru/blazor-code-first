using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using BlazorCodeFirst.Site.Layout;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// The two bodies the documentation routes share, one per language edition.
/// </summary>
/// <remarks>
/// A route template must be a compile-time constant, so each language needs a page component of its
/// own to carry its literal prefix. What the page renders is the same in every edition, which is
/// exactly what a <c>[ViewPart]</c> is for: the frames expand into each caller's generated
/// RenderView, with no component boundary, no parameters, and no second lifecycle between the page
/// and its content. The site uses the feature it documents.
///
/// Every string here comes from <see cref="Docs.Shell"/> rather than from a literal, so adding a
/// language means adding a content directory rather than editing this file.
/// </remarks>
internal static class DocsView
{
    /// <summary>One document, with the rail beside it.</summary>
    /// <remarks>
    /// entry.Html is build-time converted, repository-owned Markdown: the Html.Raw trust boundary.
    ///
    /// The rail is placed here rather than in the layout, so it renders on the documentation routes
    /// and nowhere else. It follows the document in source order, and css/app.css lifts it into the
    /// first column at wide widths. Written the other way round, a reader on a phone would meet the
    /// whole table of contents before the page they asked for.
    ///
    /// The article carries its own lang. On the canonical edition that agrees with the &lt;html lang&gt;
    /// baked into index.html and costs nothing; on a translation it is what makes the claim true,
    /// which is WCAG 3.1.2 (Language of Parts). The shell around it stays English either way, so
    /// marking the part rather than the page is also the accurate description.
    ///
    /// A search engine reads none of that. It takes the language from the visible text, and Google
    /// documents that it ignores a lang attribute outright; the hreflang set <see cref="SiteMeta"/>
    /// declares is what tells it these two routes are editions of one page. So the &lt;html lang&gt;
    /// on a translation stays English on purpose, and rewriting the published HTML per route would
    /// buy nothing a reader or a crawler can use.
    /// </remarks>
    [ViewPart]
    public static View Document(DocEntry entry) =>
        Div.Class("shell")[
            Div.Class("docs-shell")[
                Article.Class("prose docs-content").Attr("lang", entry.Lang)[
                    Component<PageTitle>()[entry.Title],
                    Component<SiteMeta>()
                        .Param(m => m.Title, entry.Title)
                        .Param(m => m.Description, entry.Description)
                        .Param(m => m.Path, DocsNav.PathOf(entry.Lang, entry.Slug))
                        .Param(m => m.Lang, entry.Lang)
                        .Param(m => m.Alternates, DocsNav.Editions(entry.Lang, entry.Slug)),
                    StaleNotice(entry),
                    H1[entry.Title],
                    // Above the document, because it is how a reader reaches a section rather than
                    // part of what the section says. It renders on the documents that publish
                    // diagnostic ids and on no others; AnchorFilter says why nothing here names
                    // which document that is.
                    If(AnchorFilter.Applies(entry),
                        () => Component<AnchorFilter>().Param(f => f.Entry, entry)),
                    Raw(entry.Html)],
                Component<DocsNav>()
                    .Param(n => n.Lang, entry.Lang)
                    .Param(n => n.Slug, entry.Slug)]];

    /// <summary>One language's documents, in reading order, with the rail beside them.</summary>
    /// <remarks>
    /// The rail renders here as well as on a document, which is what makes each slug's href appear
    /// exactly twice on an index route -- once in the rail, once in the list below. The site workflow
    /// asserts that count, because a "contains this href" check would pass even if the list rendered
    /// nothing.
    /// </remarks>
    [ViewPart]
    public static View Index(string lang)
    {
        // Read once. Docs.Shell is a generated switch that allocates a fresh ShellText and its group
        // array on every call, measured at 240 bytes, and this part asks it for five strings.
        var shell = Docs.Shell(lang);

        return Div.Class("shell")[
            Div.Class("docs-shell")[
                Section.Class("prose docs-content").Attr("lang", lang)[
                    Component<PageTitle>()[shell.IndexTitle],
                    Component<SiteMeta>()
                        .Param(m => m.Title, shell.IndexTitle)
                        .Param(m => m.Description, shell.IndexDescription)
                        .Param(m => m.Path, DocsNav.PathOf(lang, null))
                        .Param(m => m.Lang, lang)
                        .Param(m => m.Alternates, DocsNav.Editions(lang, null)),
                    H1[shell.IndexTitle],
                    P[shell.IndexLead],
                    ForEach(
                        Docs.GroupsFor(lang),
                        key: g => g,
                        content: g => Section.Class("index-group")[
                            H2[Docs.GroupLabel(lang, g)],
                            Ul.Class("index-list")[
                                ForEach(
                                    Docs.ForGroup(lang, g),
                                    key: d => d.Slug,
                                    content: d => Li[
                                        A.Href(Docs.Href(lang, d.Slug)).Class("index-link")[
                                            d.Title,
                                            Span[Docs.Href(lang, d.Slug)]]])]])],
                Component<DocsNav>()
                    .Param(n => n.Lang, lang)
                    .Param(n => n.Slug, null)]];
    }

    /// <summary>
    /// The note a translation carries when the document it follows has moved on without it.
    /// </summary>
    /// <remarks>
    /// It renders above the h1, not below the article: a reader deciding whether to trust what they
    /// are about to read needs to know before reading it, not after. The link goes to the canonical
    /// document, which is by definition the one that is current.
    ///
    /// Nothing renders when the translation is current, and nothing can render on a canonical
    /// document, which has nothing to fall behind and therefore has no text for this.
    /// </remarks>
    [ViewPart]
    private static View StaleNotice(DocEntry entry) =>
        If(entry.Stale,
            // Both strings are the translation's own, so neither overrides the lang it inherits: the
            // link points at the English page but the words offering it are the ones this reader
            // reads.
            () => Aside.Class("stale-note")[
                Span[Docs.Shell(entry.Lang).StaleNotice ?? ""],
                A.Href(Docs.Href(Docs.Canonical, entry.Slug))[
                    Docs.Shell(entry.Lang).StaleLink ?? ""]]);
}

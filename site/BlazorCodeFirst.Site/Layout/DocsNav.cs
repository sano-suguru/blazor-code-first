using System.Collections.Immutable;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// The documentation rail: one language's documents, in reading order, with the current one marked,
/// under a link to whichever other editions have the page the reader is on.
/// </summary>
/// <remarks>
/// This used to be part of <see cref="SiteNav"/>, which put the whole table of contents on the home
/// page and on the counter demo. It renders only on the documentation routes now, which is why it is
/// placed by those pages rather than by the layout.
///
/// That placement has a consequence the site workflow depends on: it asserts that each document's
/// href appears exactly twice on an index route -- once here, once in the index body. If the rail
/// moved back into the layout, the count on every other route would change too.
///
/// Unlike <see cref="SiteNav"/> this component is not in the persistent layout, so it is re-mounted
/// whenever the route moves between an index and a document. It still subscribes: navigating from
/// one document to another keeps the page component -- and therefore this -- mounted, and only a
/// parameter change would re-render it. The subscription is what makes the active mark follow that
/// move, and Dispose is what keeps it from outliving the mount.
/// </remarks>
public sealed partial class DocsNav : BodyComponentBase, IDisposable
{
    /// <summary>Which edition the rail is listing.</summary>
    /// <remarks>
    /// Passed by the page rather than parsed back out of the URL. The page knows it as a literal in
    /// its own route template, and re-deriving it here would mean writing a second, independent
    /// answer to a question that already has one.
    /// </remarks>
    [Parameter]
    public string Lang { get; set; } = Docs.Canonical;

    /// <summary>The document being read, or null on an index route.</summary>
    [Parameter]
    public string? Slug { get; set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
        InvokeAsync(StateHasChanged);

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    /// <summary>The id tying a group's heading to the list it names.</summary>
    /// <remarks>
    /// The heading is a paragraph rather than an h3, because the rail is navigation and not part of
    /// the document's outline. That leaves the list with no accessible name of its own, which is
    /// what this repairs: without it a screen reader reads three unnamed nested lists, and the
    /// grouping exists only for people who can see it.
    /// </remarks>
    private static string GroupHeadingId(string group) => "rail-group-" + group;

    // The rail once carried a "Guide" label above the list. Three named groups replaced the one
    // thing it said, and an uppercase micro-label above three more labels is a stack of headings
    // where the reader needs one. It survives as the nav's accessible name, which is the job it was
    // actually doing.
    protected override View Body =>
        Nav.Class("docs-rail").Attr("aria-label", Docs.Shell(Lang).RailHeading)[
            LanguageSwitch(Lang, Slug),
            Ul.Class("rail-groups")[
                ForEach(
                    Docs.GroupsFor(Lang),
                    key: g => g,
                    content: g => Li.Class("rail-group")[
                        P.Class("rail-group-heading").Id(GroupHeadingId(g))[Docs.GroupLabel(Lang, g)],
                        Ul.Class("rail-list").Attr("aria-labelledby", GroupHeadingId(g))[
                            ForEach(
                                Docs.ForGroup(Lang, g),
                                key: d => d.Slug,
                                content: d => Li[
                                    A.Href(Docs.Href(Lang, d.Slug))
                                        .Class(LinkClass(Docs.Href(Lang, d.Slug)))[d.Title]])]])]];

    /// <summary>
    /// A link to each other edition that has the page the reader is on, and nothing when none does.
    /// </summary>
    /// <remarks>
    /// The counterpart is checked rather than assumed. A translation is allowed to lag the canonical
    /// language by whole documents, so offering a switch on every page would hand a reader a link to
    /// a route that was never generated -- a 404 reached by following the site's own navigation,
    /// which is worse than never having offered it.
    ///
    /// Each label is written in the language it names, because it is read by someone who is not
    /// currently reading that language and is looking for the word they know. That is also why each
    /// carries its own lang: the surrounding rail is in a different one.
    ///
    /// A view part rather than an instance method, so the rail's Body stays one statically
    /// analyzable expression; the state it needs is passed rather than read off this component.
    /// </remarks>
    [ViewPart]
    private static View LanguageSwitch(string lang, string? slug) =>
        If(Counterparts(lang, slug).Count > 0,
            () => Ul.Class("lang-switch").Attr("aria-label", Docs.Shell(lang).LanguageLabel)[
                ForEach(
                    Counterparts(lang, slug),
                    key: l => l,
                    content: l => Li[
                        A.Href(slug is null ? Docs.RoutePrefix(l) : Docs.Href(l, slug))
                            .Class("lang-link")
                            .Lang(l)[Docs.Shell(l).Name]])]);

    /// <summary>The other editions that can show what the reader is looking at.</summary>
    private static List<string> Counterparts(string lang, string? slug) =>
        Counterparts(Docs.All, lang, slug);

    /// <summary>The same decision, over a manifest given directly rather than this build's.</summary>
    /// <remarks>
    /// The documents are the parameter and the language set is not, because only the documents vary
    /// in the case this exists for: a document no other edition has translated, which site/content
    /// cannot hold. DocsNavTests says why, and holds that case (#279).
    /// </remarks>
    internal static List<string> Counterparts(ImmutableArray<DocEntry> docs, string lang, string? slug)
    {
        var others = new List<string>();
        foreach (string other in Docs.Languages)
        {
            if (string.Equals(other, lang, StringComparison.Ordinal))
            {
                continue;
            }

            // An index route is offered every other edition: Docs.Languages already excludes an
            // edition with no documents, so nothing here has to re-check that. A document route
            // needs the narrower thing, that this edition has translated this particular document.
            if (slug is null || Docs.Find(docs, other, slug) is not null)
            {
                others.Add(other);
            }
        }

        return others;
    }

    /// <summary>The same, over the manifest this build produced.</summary>
    /// <remarks>
    /// Internal rather than private, because <see cref="Pages.DocsView"/> names it from a
    /// <c>[ViewPart]</c>, which expands into its caller's generated RenderView: a private member is
    /// unreachable from there and fails with BCF1002. It sits beside the decision rather than in
    /// DocsView, so the manifest-supplying wrapper is at the same address as the decision it wraps.
    /// </remarks>
    internal static List<DocAlternate> Editions(string lang, string? slug) =>
        Editions(Docs.All, lang, slug);

    /// <summary>Every edition that can show what the reader is looking at, this one included.</summary>
    /// <remarks>
    /// The language switch asks for the OTHER editions, because it offers somewhere else to go; an
    /// hreflang set asks for all of them, for the reason <see cref="SiteMeta.Tags"/> gives. One
    /// decision, two readings, so
    /// <see cref="Counterparts(ImmutableArray{DocEntry}, string, string?)"/> stays the one place that
    /// decides which editions have this document.
    /// </remarks>
    internal static List<DocAlternate> Editions(ImmutableArray<DocEntry> docs, string lang, string? slug)
    {
        var editions = new List<DocAlternate> { new(lang, PathOf(lang, slug)) };
        foreach (string other in Counterparts(docs, lang, slug))
        {
            editions.Add(new DocAlternate(other, PathOf(other, slug)));
        }

        return editions;
    }

    /// <summary>The route one edition is served from, in the form sitemap.xml declares.</summary>
    /// <remarks>
    /// With a trailing slash, which the hrefs in the rail do not carry. <see cref="SiteMeta.Path"/>
    /// says why that form and no other.
    /// </remarks>
    internal static string PathOf(string lang, string? slug) =>
        slug is null ? Docs.RoutePrefix(lang) + "/" : Docs.Href(lang, slug) + "/";

    /// <summary>The current route as a normalized absolute path.</summary>
    /// <remarks>
    /// Derived from ToBaseRelativePath for the same reason <see cref="SiteNav"/> does: the
    /// prerenderer serves the app from a random localhost port, so an absolute comparison would
    /// behave differently in the published HTML than in the browser.
    /// </remarks>
    private string CurrentPath()
    {
        string relative = Navigation.ToBaseRelativePath(Navigation.Uri);

        int cut = relative.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            relative = relative[..cut];
        }

        relative = relative.TrimEnd('/');
        return relative.Length == 0 ? "/" : "/" + relative;
    }

    // The class value is exactly "nav-link active", with no third class: the CI guard matches the
    // whole attribute value, so appending a rail-specific class would turn that assertion into a
    // silent no-op. The rail's own look comes from ".rail-list .nav-link" in css/app.css instead.
    // Exactly one element per route may carry this value, and on a document route it is this one --
    // SiteNav's "/docs" link matches exactly, so it does not light up under "/docs/{slug}", and the
    // language switch uses "lang-link" rather than "nav-link" for the same reason.
    private string LinkClass(string path) =>
        string.Equals(CurrentPath(), path, StringComparison.OrdinalIgnoreCase)
            ? "nav-link active"
            : "nav-link";
}

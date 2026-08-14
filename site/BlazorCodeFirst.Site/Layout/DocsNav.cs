using System.Collections.Immutable;
using BlazorCodeFirst;
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

    protected override View Body =>
        Nav.Class("docs-rail").Attr("aria-label", Docs.Shell(Lang).RailHeading)[
            LanguageSwitch(Lang, Slug),
            P.Class("rail-heading")[Docs.Shell(Lang).RailHeading],
            Ul.Class("rail-list")[
                ForEach(
                    Docs.ForLang(Lang),
                    key: d => d.Slug,
                    content: d => Li[
                        A.Href(Docs.Href(Lang, d.Slug))
                            .Class(LinkClass(Docs.Href(Lang, d.Slug)))[d.Title]])]];

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
                            .Attr("lang", l)[Docs.Shell(l).Name]])]);

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

            // An index route needs only that the edition exists; a document needs that edition to
            // have translated this particular document. #356 asks whether the first of those can be
            // false at all, since Docs.Languages already excludes an edition with no documents.
            bool reachable = slug is null
                ? Docs.ForLang(docs, other).Length > 0
                : Docs.Find(docs, other, slug) is not null;
            if (reachable)
            {
                others.Add(other);
            }
        }

        return others;
    }

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

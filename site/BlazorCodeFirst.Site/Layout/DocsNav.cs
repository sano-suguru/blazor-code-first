using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// The documentation rail: every document, in reading order, with the current one marked.
/// </summary>
/// <remarks>
/// This used to be part of <see cref="SiteNav"/>, which put the whole table of contents on the home
/// page and on the counter demo. It renders only on "/docs" and "/docs/{slug}" now, which is why it
/// is placed by those two pages rather than by the layout.
///
/// That placement has a consequence the site workflow depends on: it asserts that each document's
/// href appears exactly twice on "/docs" — once here, once in the index body. If the rail moved back
/// into the layout, the count on every other route would change too.
///
/// Unlike <see cref="SiteNav"/> this component is not in the persistent layout, so it is re-mounted
/// whenever the route moves between the index and a document. It still subscribes: navigating from
/// one document to another keeps DocsPage — and therefore this — mounted, and only a parameter
/// change would re-render it. The subscription is what makes the active mark follow that move, and
/// Dispose is what keeps it from outliving the mount.
/// </remarks>
public sealed partial class DocsNav : BodyComponentBase, IDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
        InvokeAsync(StateHasChanged);

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    protected override View Body =>
        Nav.Class("docs-rail").Attr("aria-label", "Documentation")[
            P.Class("rail-heading")["Guide"],
            Ul.Class("rail-list")[
                ForEach(
                    Docs.ForLang(Docs.Canonical),
                    key: d => d.Slug,
                    content: d => Li[
                        A.Href($"/docs/{d.Slug}")
                            .Class(LinkClass($"/docs/{d.Slug}"))[d.Title]])]];

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
    // SiteNav's "/docs" link matches exactly, so it does not light up under "/docs/{slug}".
    private string LinkClass(string path) =>
        string.Equals(CurrentPath(), path, StringComparison.OrdinalIgnoreCase)
            ? "nav-link active"
            : "nav-link";
}

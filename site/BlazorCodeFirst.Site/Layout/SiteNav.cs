using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// The site-level navigation: wordmark hard left, utilities hard right, nothing between them.
/// </summary>
/// <remarks>
/// The shape is deliberate. The wordmark-left / centred-link-cluster / filled-button-right bar is
/// the most-copied marketing header there is, and it says nothing about what kind of site it sits
/// on. This site has exactly three routes a reader navigates to by name, so the bar carries three
/// links and no cluster; the documentation's own navigation is the rail beside the prose, which
/// renders only on the documentation routes.
///
/// The element keeps the class "site-nav" because the site workflow asserts its presence on every
/// prerendered route as the signal that the shell rendered at all.
///
/// The wordmark link is wrapped in a div rather than carrying the brand styling itself: the CI
/// guard matches class="nav-link active" as a whole attribute value, so a third class on that
/// element would turn the guard into a silent no-op.
///
/// The component holds no state field for the current location. Body reads
/// <see cref="NavigationManager.Uri"/> directly as a pure projection, BCF3001 forbids mutation
/// inside Body, not reads, so there is no second source of truth to keep in sync. The
/// LocationChanged subscription lives in OnInitialized (outside Body) and only requests a re-render.
///
/// This component lives in the persistent layout, so it is never re-mounted and is long-lived: the
/// subscription MUST be released in Dispose or handlers accumulate and leak.
/// </remarks>
public sealed partial class SiteNav : BodyComponentBase, IDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

    // LocationChanged normally fires on Blazor's synchronization context, but dispatch through
    // InvokeAsync so a notification raised off-context (for example around the WASM hydration
    // boundary) still re-renders safely.
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) =>
        InvokeAsync(StateHasChanged);

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;

    protected override View Body =>
        Nav.Class("site-nav")[
            Div.Class("brand")[
                A.Href("/").Class(LinkClass("/"))["BlazorCodeFirst"]],
            Div.Class("nav-utilities")[
                A.Href("/docs").Class(DocsLinkClass())["Docs"],
                A.Href("/counter").Class(LinkClass("/counter"))["Demo"],
                A.Href("https://github.com/sano-suguru/blazor-code-first")
                    .Class("chip")
                    .Attr("rel", "noopener")["Source"]]];

    /// <summary>The current route as a normalized absolute path ("/", "/counter", "/docs/x").</summary>
    /// <remarks>
    /// Must be derived from ToBaseRelativePath, never from the absolute Uri string: during build-time
    /// prerendering the app is hosted on http://localhost:&lt;random port&gt;, so absolute comparisons
    /// would behave differently in the prerendered HTML than in the browser.
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

    // Exact match only: a prefix match would light up "/" (Home) on every route, and would light up
    // "/docs" on every "/docs/{slug}" route. On a document route the active link is the rail's, not
    // this one's, and the CI guard asserting exactly one active link per route is what keeps the two
    // from both claiming it.
    private string LinkClass(string path) =>
        string.Equals(CurrentPath(), path, StringComparison.OrdinalIgnoreCase)
            ? "nav-link active"
            : "nav-link";

    /// <summary>The Docs entry, which is active on every language's documentation index.</summary>
    /// <remarks>
    /// This link names the documentation index, and each language has one, so "/docs/ja" lights it up
    /// as "/docs" does. Without this, "/docs/ja" would be the only prerendered route with no active
    /// nav link at all: the rail lists documents, so nothing there matches an index either.
    ///
    /// Still an exact match per index rather than a prefix match on "/docs". A prefix would claim the
    /// active mark on every document route as well, where it belongs to the rail, and the CI guard
    /// asserting exactly one active link per route is what keeps the two from both taking it.
    /// </remarks>
    private string DocsLinkClass()
    {
        string current = CurrentPath();
        foreach (string lang in Docs.Languages)
        {
            if (string.Equals(current, Docs.RoutePrefix(lang), StringComparison.OrdinalIgnoreCase))
            {
                return "nav-link active";
            }
        }

        return "nav-link";
    }
}

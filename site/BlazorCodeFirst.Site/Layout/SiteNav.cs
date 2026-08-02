using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// The site navigation, including the active state of the current route.
/// </summary>
/// <remarks>
/// The component holds no state field for the current location. Body reads
/// <see cref="NavigationManager.Uri"/> directly as a pure projection — BCF3001 forbids mutation
/// inside Body, not reads — so there is no second source of truth to keep in sync. The
/// LocationChanged subscription lives in OnInitialized (outside Body) and only requests a re-render.
///
/// This component lives in the persistent layout, so it is never re-mounted and is long-lived: the
/// subscription MUST be released in Dispose or handlers accumulate and leak.
/// </remarks>
public sealed partial class SiteNav : ComposeComponentBase, IDisposable
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
            Ul.Class("nav-list")[
                Li[A.Href("/").Class(LinkClass("/"))["Home"]],
                Li[A.Href("/counter").Class(LinkClass("/counter"))["Counter"]],
                Li[A.Href("/docs").Class(LinkClass("/docs"))["Docs"]],
                ForEach(
                    Docs.All,
                    key: d => d.Slug,
                    content: d => Li[
                        A.Href($"/docs/{d.Slug}")
                            .Class(LinkClass($"/docs/{d.Slug}"))[d.Title]])]];

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
    // "/docs" on every "/docs/{slug}" route. DocLinkClass used to special-case "/docs" to activate
    // the default document's link; with "/docs" an index page of its own that case is gone, and the
    // CI guard asserting exactly one active nav link per route is what keeps it from coming back.
    private string LinkClass(string path) =>
        string.Equals(CurrentPath(), path, StringComparison.OrdinalIgnoreCase)
            ? "nav-link active"
            : "nav-link";
}

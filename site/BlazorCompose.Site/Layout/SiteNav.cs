using BlazorCompose;
using BlazorCompose.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Layout;

/// <summary>
/// The site navigation, including the active state of the current route.
/// </summary>
/// <remarks>
/// The component holds no state field for the current location. Body reads
/// <see cref="NavigationManager.Uri"/> directly as a pure projection — BC3001 forbids mutation
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
        Nav(
            Ul(
                Li(A("Home").Href("/").Class(LinkClass("/"))),
                Li(A("Counter").Href("/counter").Class(LinkClass("/counter"))),
                ForEach(
                    Docs.All,
                    key: d => d.Slug,
                    content: d => Li(A(d.Title).Href($"/docs/{d.Slug}").Class(DocLinkClass(d.Slug)))))
            .Class("nav-list"))
        .Class("site-nav");

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

    // Exact match only: a prefix match would light up "/" (Home) on every route.
    private string LinkClass(string path) =>
        string.Equals(CurrentPath(), path, StringComparison.OrdinalIgnoreCase)
            ? "nav-link active"
            : "nav-link";

    // "/docs" renders the default document (lowest Order), so it activates that document's link.
    private string DocLinkClass(string slug)
    {
        string current = CurrentPath();
        bool isDefaultDoc = string.Equals(current, "/docs", StringComparison.OrdinalIgnoreCase)
            && Docs.All.Length > 0
            && string.Equals(Docs.All[0].Slug, slug, StringComparison.OrdinalIgnoreCase);

        return isDefaultDoc || string.Equals(current, $"/docs/{slug}", StringComparison.OrdinalIgnoreCase)
            ? "nav-link active"
            : "nav-link";
    }
}

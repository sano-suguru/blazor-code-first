using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using BlazorCodeFirst.Site.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// Renders one documentation page. A single generic page serves every document: the DocGen manifest
/// is the single source of truth for routes, navigation, and prerendered paths.
/// </summary>
[Route("/docs/{Slug}")]
public sealed partial class DocsPage : BodyComponentBase
{
    /// <summary>The requested document's slug, taken from the route.</summary>
    /// <remarks>
    /// Declared nullable even though the single route template always supplies it: Blazor assigns
    /// route parameters by reflection after construction, so the property is null until the first
    /// assignment. <see cref="Docs.Find"/> accepts null and returns null, which renders the same
    /// not-found body as an unknown slug.
    /// </remarks>
    [Parameter]
    public string? Slug { get; set; }

    private string _title = "";
    private string _html = "";
    private bool _found;

    protected override void OnParametersSet()
    {
        var entry = Docs.Find(Docs.Canonical, Slug);

        _found = entry is not null;
        _title = entry?.Title ?? "Not found";
        _html = entry?.Html ?? "";
    }

    // _html is build-time converted, repository-owned Markdown: the Html.Raw trust boundary.
    //
    // The rail is placed here rather than in the layout, so it renders on the documentation routes
    // and nowhere else. The not-found branch drops it: an unknown slug is not a place in the guide.
    //
    // It follows the document in source order, and css/app.css lifts it into the first column at
    // wide widths. Written the other way round, a reader on a phone would meet the whole table of
    // contents before the page they asked for.
    protected override View Body =>
        If(_found,
            () => Div.Class("shell")[
                    Div.Class("docs-shell")[
                        Article.Class("prose docs-content")[
                            Component<PageTitle>()[_title],
                            H1[_title],
                            Raw(_html)],
                        Component<DocsNav>()]],
            // Shared with the "/404" route: after the SPA catch-all was removed, an unknown slug is
            // served as 404.html and then re-rendered here on hydration, so the two must match.
            () => NotFoundContent.NotFound());
}

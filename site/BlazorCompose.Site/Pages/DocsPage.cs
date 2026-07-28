using BlazorCompose;
using BlazorCompose.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Pages;

/// <summary>
/// Renders one documentation page. A single generic page serves every document: the DocGen manifest
/// is the single source of truth for routes, navigation, and prerendered paths.
/// </summary>
[Route("/docs/{Slug}")]
public sealed partial class DocsPage : ComposeComponentBase
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
        var entry = Docs.Find(Slug);

        _found = entry is not null;
        _title = entry?.Title ?? "Not found";
        _html = entry?.Html ?? "";
    }

    // _html is build-time converted, repository-owned Markdown: the Html.Raw trust boundary.
    protected override View Body =>
        If(_found,
            () => Article(
                    Component<PageTitle>(_title),
                    H1(_title),
                    Raw(_html))
                .Class("prose"),
            // Shared with the "/404" route: after the SPA catch-all was removed, an unknown slug is
            // served as 404.html and then re-rendered here on hydration, so the two must match.
            () => NotFoundContent.NotFound());
}

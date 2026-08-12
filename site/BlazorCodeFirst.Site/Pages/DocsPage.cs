using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// Renders one document of the canonical edition. A single generic page serves every document: the
/// DocGen manifest is the single source of truth for routes, navigation, and prerendered paths.
/// </summary>
/// <remarks>
/// One page component per language, because a route template must be a compile-time constant and
/// each edition's prefix is part of its own. The body is not duplicated with it: both pages call the
/// same <see cref="DocsView.Document"/> part, which expands into each caller's RenderView.
/// </remarks>
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

    private DocEntry? _entry;

    protected override void OnParametersSet() => _entry = Docs.Find(Docs.Canonical, Slug);

    protected override View Body =>
        If(_entry is not null,
            () => DocsView.Document(_entry!),
            // Shared with the "/404" route: after the SPA catch-all was removed, an unknown slug is
            // served as 404.html and then re-rendered here on hydration, so the two must match.
            () => NotFoundContent.NotFound());
}

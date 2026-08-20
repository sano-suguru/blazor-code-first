using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// The canonical edition's index. "/docs" used to be a second route on <see cref="DocsPage"/> that
/// rendered the lowest-order document, so it returned content identical to "/docs/getting-started"
/// under a URL nothing linked to. It is now a page of its own with content of its own.
/// </summary>
[Route("/docs")]
public sealed partial class DocsIndexPage : BodyComponentBase
{
    protected override View Body => DocsView.Index(Docs.Canonical);
}

using BlazorCompose;
using BlazorCompose.Site.Content;
using BlazorCompose.Site.Layout;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Pages;

/// <summary>
/// Renders one documentation page. A single generic page serves every document: the DocGen manifest
/// is the single source of truth for routes, navigation, and prerendered paths.
/// </summary>
[Route("/docs")]
[Route("/docs/{Slug}")]
public sealed partial class DocsPage : ComposeComponentBase
{
    /// <summary>The requested document, or null on the bare "/docs" route. Blazor fills unused route
    /// parameters of a multi-template handler with null, so this does not retain a previous value.</summary>
    [Parameter]
    public string? Slug { get; set; }

    private string _title = "";
    private string _html = "";
    private bool _found;

    protected override void OnParametersSet()
    {
        // "/docs" keeps working as a published URL by showing the default (lowest Order) document.
        var entry = Slug is null
            ? (Docs.All.Length > 0 ? Docs.All[0] : null)
            : Docs.Find(Slug);

        _found = entry is not null;
        _title = entry?.Title ?? "Not found";
        _html = entry?.Html ?? "";
    }

    // _html is build-time converted, repository-owned Markdown: the Html.Raw trust boundary.
    protected override View Body =>
        If(_found,
            () => Article(
                    Component<DocTitle>().Param(t => t.Title, _title),
                    H1(_title),
                    Raw(_html))
                .Class("prose"),
            () => Section(
                    Component<DocTitle>().Param(t => t.Title, _title),
                    H1(_title),
                    P("The requested document does not exist."))
                .Class("prose"));
}

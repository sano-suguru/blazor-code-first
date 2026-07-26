using BlazorCompose;
using BlazorCompose.Site.Content;
using BlazorCompose.Site.Layout;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Pages;

/// <summary>
/// The documentation index. "/docs" used to be a second route on <see cref="DocsPage"/> that
/// rendered the lowest-order document, so it returned content identical to "/docs/getting-started"
/// under a URL nothing linked to. It is now a page of its own with content of its own.
/// </summary>
[Route("/docs")]
public sealed partial class DocsIndexPage : ComposeComponentBase
{
    protected override View Body =>
        Section(
            Component<DocTitle>().Param(t => t.Title, "Documentation"),
            H1("Documentation"),
            P("Every document in the guide, in reading order."),
            Ul(
                ForEach(
                    Docs.All,
                    key: d => d.Slug,
                    content: d => Li(A(d.Title).Href($"/docs/{d.Slug}")))))
        .Class("prose");
}

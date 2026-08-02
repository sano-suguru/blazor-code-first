using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// The documentation index. "/docs" used to be a second route on <see cref="DocsPage"/> that
/// rendered the lowest-order document, so it returned content identical to "/docs/getting-started"
/// under a URL nothing linked to. It is now a page of its own with content of its own.
/// </summary>
[Route("/docs")]
public sealed partial class DocsIndexPage : ComposeComponentBase
{
    protected override View Body =>
        Section.Class("prose")[
            Component<PageTitle>()["Documentation"],
            H1["Documentation"],
            P["Every document in the guide, in reading order."],
            Ul[
                ForEach(
                    Docs.All,
                    key: d => d.Slug,
                    content: d => Li[A.Href($"/docs/{d.Slug}")[d.Title]])]];
}

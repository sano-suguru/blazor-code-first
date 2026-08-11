using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using BlazorCodeFirst.Site.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// The documentation index. "/docs" used to be a second route on <see cref="DocsPage"/> that
/// rendered the lowest-order document, so it returned content identical to "/docs/getting-started"
/// under a URL nothing linked to. It is now a page of its own with content of its own.
/// </summary>
/// <remarks>
/// The rail renders here as well as on a document, which is what makes each slug's href appear
/// exactly twice on this route — once in the rail, once in the list below. The site workflow asserts
/// that count, because a "contains this href" check would pass even if the list rendered nothing.
/// </remarks>
[Route("/docs")]
public sealed partial class DocsIndexPage : BodyComponentBase
{
    protected override View Body =>
        Div.Class("shell")[
            Div.Class("docs-shell")[
                Section.Class("prose docs-content")[
                    Component<PageTitle>()["Documentation"],
                    H1["Documentation"],
                    P["Every document in the guide, in reading order."],
                    Ul.Class("index-list")[
                        ForEach(
                            Docs.All,
                            key: d => d.Slug,
                            content: d => Li[
                                A.Href($"/docs/{d.Slug}").Class("index-link")[
                                    d.Title,
                                    Span["/docs/" + d.Slug]]])]],
                Component<DocsNav>()]];
}

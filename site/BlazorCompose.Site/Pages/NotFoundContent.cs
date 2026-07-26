using BlazorCompose;
using BlazorCompose.Site.Layout;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Pages;

/// <summary>
/// The not-found body, shared by the prerendered "/404" route and by <see cref="DocsPage"/>'s
/// unknown-slug branch.
/// </summary>
/// <remarks>
/// The two must render the same markup. With the SPA catch-all gone, a request for
/// "/docs/unknown" is answered with 404.html -- the prerendered "/404" -- but once hydration
/// completes the router matches "/docs/{Slug}" and renders DocsPage's not-found branch instead. If
/// the two differed, the page would visibly swap content on hydration.
///
/// A <c>[Composable]</c> is what makes sharing possible without a wrapper component: the generator
/// expands this body statically into every caller's RenderBody, so both components emit the same
/// RenderTreeBuilder sequence with no runtime indirection. It takes no parameters and touches no
/// private or protected member, so no accessibility requirement restricts where it may expand.
/// </remarks>
public static class NotFoundContent
{
    [Composable]
    public static View NotFound() =>
        Section(
            Component<DocTitle>().Param(t => t.Title, "Not found"),
            H1("Page not found"),
            P("The requested page does not exist."),
            P(A("Browse the documentation").Href("/docs"), " or ", A("go to the home page").Href("/")))
        .Class("prose");
}

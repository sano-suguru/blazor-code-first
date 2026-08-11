using BlazorCodeFirst;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

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
/// expands this body statically into every caller's RenderView, so both components emit the same
/// RenderTreeBuilder sequence with no runtime indirection. It takes no parameters and touches no
/// private or protected member, so no accessibility requirement restricts where it may expand.
/// </remarks>
public static class NotFoundContent
{
    [Composable]
    public static View NotFound() =>
        Div.Class("shell")[
            Section.Class("prose demo")[
                Component<PageTitle>()["Not found"],
                H1["Page not found"],
                P["The requested page does not exist."],
                P[A.Href("/docs")["Browse the documentation"], " or ", A.Href("/")["go to the home page"]]]];
}

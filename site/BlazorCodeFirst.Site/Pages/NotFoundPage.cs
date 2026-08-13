using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// The "/404" route, whose prerendered output is copied to the publish root as <c>404.html</c>,
/// the file Cloudflare Workers serves for an unmatched request under
/// <c>not_found_handling: "404-page"</c>.
/// </summary>
/// <remarks>
/// This route is also what <c>Router.NotFoundPage</c> renders for an unmatched client-side
/// navigation, so a hard request for an unknown path and a link click to one show the same page.
///
/// "/404" is also reachable as an ordinary 200 page: under the default <c>html_handling</c>,
/// Workers serves <c>404.html</c> for "/404" and redirects "/404.html" to it. The <c>_headers</c>
/// rule for "/404" is what keeps that 200 out of search indexes.
/// </remarks>
[Route("/404")]
public sealed partial class NotFoundPage : BodyComponentBase
{
    protected override View Body => NotFoundContent.NotFound();
}

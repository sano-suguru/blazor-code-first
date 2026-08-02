using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// The "/404" route, whose prerendered output is copied to the publish root as <c>404.html</c> —
/// the file that makes Cloudflare Pages serve real 404 responses instead of assuming a
/// single-page application.
/// </summary>
/// <remarks>
/// This route is also what <c>Router.NotFoundPage</c> renders for an unmatched client-side
/// navigation, so a hard request for an unknown path and a link click to one show the same page.
///
/// "/404" is expected to remain reachable as an ordinary 200 page: Cloudflare redirects
/// "/404.html" to its extension-less counterpart and serves the file matching "/404". Cloudflare
/// documents both redirect steps but not the resulting status code, so the <c>_headers</c> rule for
/// "/404" exists to keep that page out of search indexes if — as measured — it is a 200.
/// </remarks>
[Route("/404")]
public sealed partial class NotFoundPage : BodyComponentBase
{
    protected override View Body => NotFoundContent.NotFound();
}

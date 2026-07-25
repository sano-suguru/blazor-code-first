using Microsoft.AspNetCore.Components;
using BlazorCompose;
using BlazorCompose.Site.Content;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Pages;

[Route("/docs")]
public partial class DocsPage : ComposeComponentBase
{
    // Docs.GettingStarted is trusted, build-time-generated HTML (repo-owned Markdown,
    // converted and highlighted by the DocGen tool at build time) — the Html.Raw trust boundary.
    protected override View Body =>
        Main(Raw(Docs.GettingStarted));
}

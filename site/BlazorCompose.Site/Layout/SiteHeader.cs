using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Layout;

public sealed partial class SiteHeader : ComposeComponentBase
{
    protected override View Body =>
        Header(
            A("BlazorCompose").Href("/").Class("brand"),
            Span("Declarative UI for Blazor, in plain C#").Class("tagline"))
        .Class("site-header");
}

using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Layout;

/// <summary>
/// The site shell. Written in Compose: the chrome is the design-time Chrome expression, and Body is
/// Blazor's routed page content placed as element content.
/// </summary>
public sealed partial class MainLayout : ComposeLayoutBase
{
    protected override View Chrome =>
        Div(
            Component<SiteHeader>(),
            Div(
                Component<SiteNav>(),
                Main(Body).Class("site-main"))
            .Class("site-body"),
            Component<SiteFooter>())
        .Class("site-shell");
}

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
        Div.Class("site-shell")[
            Component<SiteHeader>(),
            Div.Class("site-body")[
                Component<SiteNav>(),
                Main.Class("site-main")[Body]],
            Component<SiteFooter>()];
}

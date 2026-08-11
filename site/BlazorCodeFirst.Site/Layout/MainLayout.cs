using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// The site shell. Written in BlazorCodeFirst: the chrome is the design-time Chrome expression, and Body is
/// Blazor's routed page content placed as element content.
/// </summary>
/// <remarks>
/// The shell is header, main, footer, and nothing else. The documentation rail used to live here,
/// which meant the whole table of contents rendered on the home page and on the counter demo; it is
/// now placed by the two documentation pages that need it (<see cref="DocsNav"/>).
///
/// The id on the main landmark is the skip link's target, and every page's own container supplies
/// its width — so a landing page can run edge to edge while a document stays inside a measure.
/// </remarks>
public sealed partial class MainLayout : ChromeLayoutBase
{
    protected override View Chrome =>
        Div.Class("site-shell")[
            Component<SiteHeader>(),
            Main.Id("content").Class("site-main")[Body],
            Component<SiteFooter>()];
}

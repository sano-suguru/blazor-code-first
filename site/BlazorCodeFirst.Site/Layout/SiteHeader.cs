using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// The banner landmark, and the skip link that precedes it.
/// </summary>
/// <remarks>
/// The skip link is the first focusable element on every page, and it targets the id that
/// <see cref="MainLayout"/> puts on the main landmark. It is visually hidden until focused, which
/// is the only state in which it is useful.
/// </remarks>
public sealed partial class SiteHeader : BodyComponentBase
{
    protected override View Body =>
        Fragment(
            A.Href("#content").Class("skip-link")["Skip to content"],
            Header.Class("site-header")[Component<SiteNav>()]);
}

using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components.Layout;

/// <summary>
/// The host's layout, written in BlazorCodeFirst. A generated layout prerendered by a real Blazor host
/// is reachable nowhere else in this repository: the bUnit tests have no HTTP pipeline and no prerender
/// pass. <see cref="Microsoft.AspNetCore.Components.LayoutComponentBase.Body"/> is Blazor's routed page
/// content, placed here as element content.
/// </summary>
public sealed partial class MainLayout : ChromeLayoutBase
{
    protected override View Chrome =>
        Div.Class("app-shell")[
            Main[Body]];
}

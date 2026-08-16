using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>layouts.md's figure for a layout's chrome.</summary>
/// <remarks>
/// The one example whose subject takes a parameter. Body is LayoutComponentBase's, holding the routed
/// page, so FigureTests supplies a stand-in for it and the output half quotes that stand-in where the
/// page would land. Everything around it is the layout's own markup, which is what the figure is for:
/// Main[Body] is exactly &lt;main&gt;@Body&lt;/main&gt;, and the output says so.
/// </remarks>
internal sealed partial class AppShell : ChromeLayoutBase
{
    // <figure>
    protected override View Chrome =>
        Div.Class("shell")[
            Header[H1["My App"]],
            Main.Class("content")[Body],
            Footer["© 2026"]];
    // </figure>
}

using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

public sealed partial class SiteHeader : ComposeComponentBase
{
    protected override View Body =>
        Header.Class("site-header")[
            A.Href("/").Class("brand")["BlazorCodeFirst"],
            Span.Class("tagline")["Declarative UI for Blazor, in plain C#"]];
}

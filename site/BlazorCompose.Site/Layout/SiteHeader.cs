using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Layout;

public sealed partial class SiteHeader : ComposeComponentBase
{
    protected override View Body =>
        Header.Class("site-header")[
            A.Href("/").Class("brand")["BlazorCompose"],
            Span.Class("tagline")["Declarative UI for Blazor, in plain C#"]];
}

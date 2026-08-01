using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Layout;

public sealed partial class SiteFooter : ComposeComponentBase
{
    protected override View Body =>
        Footer.Class("site-footer")[
            Span["This site is built with BlazorCompose."],
            A.Href("https://github.com/sano-suguru/blazor-compose")["Source on GitHub"]];
}

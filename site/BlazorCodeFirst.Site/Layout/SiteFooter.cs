using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

public sealed partial class SiteFooter : ComposeComponentBase
{
    protected override View Body =>
        Footer.Class("site-footer")[
            Span["This site is built with BlazorCodeFirst."],
            A.Href("https://github.com/sano-suguru/blazor-code-first")["Source on GitHub"]];
}

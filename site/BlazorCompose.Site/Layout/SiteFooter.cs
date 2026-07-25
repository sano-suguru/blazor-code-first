using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Layout;

public sealed partial class SiteFooter : ComposeComponentBase
{
    protected override View Body =>
        Footer(
            Span("This site is built with BlazorCompose."),
            A("Source on GitHub").Href("https://github.com/sano-suguru/blazor-compose"))
        .Class("site-footer");
}

using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>
/// One line of credits above a hairline rule. Not a sitemap.
/// </summary>
/// <remarks>
/// The four-column link index is what a footer becomes when a site has more sections than a reader
/// can hold. This one has three routes and a repository, so the footer closes the page instead of
/// cataloguing it.
/// </remarks>
public sealed partial class SiteFooter : BodyComponentBase
{
    protected override View Body =>
        Footer.Class("site-footer")[
            Div.Class("shell")[
                Span["Built with BlazorCodeFirst, and published as prerendered static HTML."],
                Div.Class("foot-links")[
                    A.Href("https://github.com/sano-suguru/blazor-code-first")
                        .Class("foot-link")
                        .Attr("rel", "noopener")["Repository"],
                    A.Href("/docs").Class("foot-link")["Documentation"],
                    Span["MIT"]]]];
}

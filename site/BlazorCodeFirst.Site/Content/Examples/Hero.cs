using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>
/// The landing page's first figure, as a component that actually renders.
/// </summary>
/// <remarks>
/// The figure claims a correspondence between a C# expression and the HTML it produces, so both
/// halves are held to this type: FigureTests compares site/snippets/hero.cs to the region below and
/// site/snippets/hero.html to what rendering this produces. A figure nobody renders is a drawing.
///
/// No [Route]. eng/verify-site-prerender.sh asserts the published route set equals the set
/// site/content backs, so an example carrying one would fail it. Routing is shown where it belongs,
/// in getting-started.md's first component.
///
/// Internal, and reached only through InternalsVisibleTo. Nothing on the site renders this type --
/// the landing page places the figure's text, not this component -- so CA1515 is right that the
/// site's own assembly has no caller for it. The test assembly is the caller, and saying so in the
/// project file is the accurate answer rather than widening the type to quiet the rule.
/// </remarks>
internal sealed partial class Hero : BodyComponentBase
{
    // <figure>
    protected override View Body =>
        Section.Class("prose")[
            H1["Blazor UI in C#"],
            P["Attributes first."],
            A.Href("/docs")["The guide"]];
    // </figure>
}

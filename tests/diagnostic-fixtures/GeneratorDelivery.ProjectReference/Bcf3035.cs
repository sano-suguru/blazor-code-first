using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3035: an event modifier with no event before it on the element.
/// </summary>
/// <remarks>
/// The element carries a decoration but no event, which is the shape an author reaches by chaining the
/// modifier onto the wrong link. Unlike its sibling BCF3038, this consults no <c>[EventHandler]</c> table,
/// so it fires in any compilation that can spell the surface at all — including one that cannot resolve
/// <c>Microsoft.AspNetCore.Components.Web</c>, which this project does reference but only for BCF3038's
/// sake.
/// </remarks>
public partial class Bcf3035Host : BodyComponentBase
{
    protected override View Body => Button.Class("go").PreventDefault()["Go"];
}

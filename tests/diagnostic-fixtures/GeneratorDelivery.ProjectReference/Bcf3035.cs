using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3035: an event modifier with no event before it on the element.
/// </summary>
/// <remarks>
/// The element carries a decoration but no event, which is the shape an author reaches by chaining the
/// modifier onto the wrong link. Unlike BCF3028's mapping half, this consults no <c>[EventHandler]</c>
/// table, so it fires in this project as it does anywhere: the fixture needs no reference to
/// <c>Microsoft.AspNetCore.Components.Web</c>, which this project deliberately does not have.
/// </remarks>
public partial class Bcf3035Host : BodyComponentBase
{
    protected override View Body => Button.Class("go").PreventDefault()["Go"];
}

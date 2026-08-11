using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3028: an event handler whose argument type is not a <c>System.EventArgs</c>. <c>int</c> violates the
/// <c>where TArgs : System.EventArgs</c> constraint on <c>.On&lt;TArgs&gt;</c>, so the call binds to nothing.
/// </summary>
/// <remarks>
/// The shape whose delivery is in question, of the two BCF3028 covers. C# computes CS0311 here, naming the
/// type and the constraint, and the author never receives it: no <c>RenderView</c> is generated for this
/// class, so it carries CS0534, and <c>csc</c> stops after the declaration stage without binding method
/// bodies. Measured on #155 before this diagnostic existed, the whole list was CS0534 and BCF1003. The other
/// shape, a handler whose type merely disagrees with the event's <c>[EventHandler]</c> mapping, binds and is
/// covered in-process; it also needs a reference to <c>Microsoft.AspNetCore.Components.Web</c>, which this
/// fixture deliberately does not have, and the constraint half consults no mapping at all.
/// </remarks>
public partial class Bcf3028Host : BodyComponentBase
{
    protected override View Body => Button.On("onclick", (int x) => { })["Go"];
}

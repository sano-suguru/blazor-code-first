using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3038: an event modifier the event's own <c>[EventHandler]</c> registration disables.
/// </summary>
/// <remarks>
/// <c>oncancel</c> is one of the two framework registrations that disable <c>stopPropagation</c>, measured
/// on <c>Microsoft.AspNetCore.Components.Web</c> 10.0.10. Every arm of this diagnostic reads a
/// registration, so unlike BCF3035 there is no table-free shape to deliver instead; this one asks about
/// the framework's, which is why the project references that assembly (see the csproj). A registration the
/// compilation declares itself would need no reference since #396, and is pinned in-process.
/// </remarks>
public partial class Bcf3038Host : BodyComponentBase
{
    protected override View Body => Div.On("oncancel", Cancelled).StopPropagation()["bcf3038"];

    private void Cancelled() { }
}

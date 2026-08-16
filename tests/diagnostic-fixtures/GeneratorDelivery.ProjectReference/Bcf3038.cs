using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3038: an event modifier the event's own <c>[EventHandler]</c> registration disables.
/// </summary>
/// <remarks>
/// <c>oncancel</c> is one of the two framework registrations that disable <c>stopPropagation</c>, measured
/// on <c>Microsoft.AspNetCore.Components.Web</c> 10.0.10. Every arm of this diagnostic reads that
/// assembly's registrations, so unlike BCF3035 there is no arm that could be delivered from a project
/// without it — which is why this project references it (see the csproj) and why the shape here is the
/// table-consulting one rather than a second, table-free one.
/// </remarks>
public partial class Bcf3038Host : BodyComponentBase
{
    protected override View Body => Div.On("oncancel", Cancelled).StopPropagation()["bcf3038"];

    private void Cancelled() { }
}

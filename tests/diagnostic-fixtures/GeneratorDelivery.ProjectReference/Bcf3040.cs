using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3040: <c>.FormName</c> written on a non-<c>form</c> element. <c>onsubmit</c> never fires on a
/// <c>div</c>, so the registration would always be dead.
/// </summary>
public partial class Bcf3040Host : BodyComponentBase
{
    protected override View Body => Div.FormName("save")["submit"];
}

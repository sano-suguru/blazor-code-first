using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3039: <c>.FormName</c> written with a literal empty string. <c>AddNamedEvent</c> throws
/// <c>ArgumentException</c> for an empty name at run time, so this is rejected at compile time instead.
/// </summary>
public partial class Bcf3039Host : BodyComponentBase
{
    protected override View Body => Form.FormName("")["submit"];
}

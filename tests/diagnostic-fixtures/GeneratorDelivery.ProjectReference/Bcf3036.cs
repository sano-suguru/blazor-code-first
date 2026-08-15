using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3036: the same event modifier written twice for one event.
/// </summary>
/// <remarks>
/// The two modifiers are separate channels, so the duplicate has to be the same one twice. Writing
/// <c>.PreventDefault().StopPropagation()</c> instead is legal and is covered in-process.
/// </remarks>
public partial class Bcf3036Host : BodyComponentBase
{
    protected override View Body =>
        Button.On("onclick", Go).PreventDefault().PreventDefault()["Go"];

    private static void Go()
    {
    }
}

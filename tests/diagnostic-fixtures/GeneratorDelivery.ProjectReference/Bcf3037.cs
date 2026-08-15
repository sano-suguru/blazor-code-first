using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3037: an event modifier written after a <c>.Bind</c>, whose event it cannot reach.
/// </summary>
/// <remarks>
/// The shape that made this diagnostic necessary: before it, the modifier attached to the earlier
/// <c>.On</c> and nothing was reported, so the author got a modifier on an event they did not write it
/// after. The earlier <c>.On</c> is what makes that the failure mode rather than BCF3035, and it is here
/// for that reason.
/// </remarks>
public partial class Bcf3037Host : BodyComponentBase
{
    private string _text = string.Empty;

    protected override View Body =>
        Input.On("onkeydown", Noted)
             .Bind("value", "oninput", () => _text, v => _text = v)
             .PreventDefault();

    private static void Noted()
    {
    }
}

using System.Globalization;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components;

/// <summary>
/// Two wheel targets on a page tall enough to scroll: one writes <c>.PreventDefault()</c> and the other
/// writes nothing.
/// </summary>
/// <remarks>
/// <para>
/// Whether the modifier reached the browser is not observable from frames, and no .NET-side layer
/// dispatches a real wheel event, so this is the only place the emitted
/// <c>__internal_preventDefault_onwheel</c> attribute is shown to do what it names (#368).
/// </para>
/// <para>
/// The unmodified target is what makes the assertion mean anything: without it, a page that simply could
/// not scroll would pass. The shared counter separates "preventDefault stopped the scroll" from "the wheel
/// event never reached Blazor at all", which is the failure a passive listener would produce.
/// </para>
/// </remarks>
public sealed partial class EventModifierView : BodyComponentBase
{
    private int _wheelCount;

    protected override View Body => Div[
        Div.Id("blocked")
           .Attr("style", "height: 200px; background: #eee")
           .On("onwheel", () => CountWheel()).PreventDefault()["blocked"],
        Div.Id("plain")
           .Attr("style", "height: 200px; background: #ddd")
           .On("onwheel", () => CountWheel())["plain"],
        Div.Id("filler").Attr("style", "height: 4000px"),
        Div.Id("wheel-count")[_wheelCount.ToString(CultureInfo.InvariantCulture)]
    ];

    // The wheel arguments are not read: the spec only checks that the count moved, which is what
    // separates preventDefault stopping the scroll from the event never reaching Blazor.
    private void CountWheel() => _wheelCount++;
}

using System.Globalization;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.WebAppTestHost.Components;

/// <summary>
/// Two wheel targets on a page tall enough to scroll, and two bound inputs inside counting parents: in each
/// pair one carries a modifier and the other writes nothing.
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
/// <para>
/// The input pair asks the same question of a binding's own event, which #370 raised and could not answer
/// from a measurement taken against a plain handler: the frame the modifier sits beside is a
/// <c>CreateBinder</c> callback rather than an <c>EventCallback</c> the author wrote. The modifier is
/// <c>stopPropagation</c> and not <c>preventDefault</c> because <c>input</c> is not cancelable, so a
/// <c>preventDefault</c> that arrived and was honoured would produce no observable difference. <c>input</c>
/// does bubble, so a parent handler that stops counting is the observation.
/// </para>
/// </remarks>
public sealed partial class EventModifierView : BodyComponentBase
{
    private int _wheelCount;
    private int _blockedParentCount;
    private int _plainParentCount;
    private string _blockedText = string.Empty;
    private string _plainText = string.Empty;

    protected override View Body => Div[
        Div.Id("blocked")
           .Attr("style", "height: 200px; background: #eee")
           .On("onwheel", () => CountWheel()).PreventDefault()["blocked"],
        Div.Id("plain")
           .Attr("style", "height: 200px; background: #ddd")
           .On("onwheel", () => CountWheel())["plain"],

        Div.Id("blocked-parent").On("oninput", () => _blockedParentCount++)[
            Input.Id("blocked-input")
                 .Bind("value", "oninput", () => _blockedText, v => _blockedText = v)
                 .StopPropagation()
        ],
        Div.Id("plain-parent").On("oninput", () => _plainParentCount++)[
            Input.Id("plain-input")
                 .Bind("value", "oninput", () => _plainText, v => _plainText = v)
        ],
        Div.Id("blocked-echo")[_blockedText],
        Div.Id("plain-echo")[_plainText],
        Div.Id("blocked-parent-count")[_blockedParentCount.ToString(CultureInfo.InvariantCulture)],
        Div.Id("plain-parent-count")[_plainParentCount.ToString(CultureInfo.InvariantCulture)],

        Div.Id("filler").Attr("style", "height: 4000px"),
        Div.Id("wheel-count")[_wheelCount.ToString(CultureInfo.InvariantCulture)]
    ];

    // The wheel arguments are not read: the spec only checks that the count moved, which is what
    // separates preventDefault stopping the scroll from the event never reaching Blazor.
    private void CountWheel() => _wheelCount++;
}

using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// Two bindings on one element, which BCF3021 rejected until #162. The two events are distinct, so each
/// binder gets its own event frame and its own handler ID; this component exists to show that they stay
/// separate under real dispatch.
/// </summary>
public partial class TwoBoundInputs : BodyComponentBase
{
    public string Live { get; set; } = "";

    public string Committed { get; set; } = "";

    protected override View Body =>
        Html.Input.Type("text")
            .Bind("value", "oninput", () => Live)
            .Bind("data-committed", "onchange", () => Committed);
}

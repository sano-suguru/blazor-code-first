using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The <c>bool</c> attribute value (#158) on the element path, where the value is state rather than a
/// constant. This is the shape the overload exists for: the attribute appears and disappears as the
/// state changes, without the component spelling out either outcome.
/// </summary>
public partial class BooleanAttributeComponent : BodyComponentBase
{
    private bool _disabled = true;

    protected override View Body =>
        Div[
            Input.Type("text").Attr("disabled", _disabled),
            Button.OnClick(() => _disabled = !_disabled)["toggle"]];
}

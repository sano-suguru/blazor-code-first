using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The explicit-setter path (Task 5): the setter trims the incoming value, which is what makes the
/// DOM resynchronization observable — the element's typed text and the field's normalized value
/// diverge.
/// </summary>
public partial class NormalizingTextInput : BodyComponentBase
{
    public string Name { get; set; } = "";

    protected override View Body =>
        Html.Input.Type("text").Bind("value", "oninput", () => Name, v => Name = v.Trim());
}

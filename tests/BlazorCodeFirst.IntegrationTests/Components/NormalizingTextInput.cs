using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The explicit-setter path: the setter trims the incoming value. The divergence between the typed text
/// and the normalized field that <c>SetUpdatesAttributeName</c> resyncs is not reachable from bUnit
/// (measured): bUnit's <c>Input()</c> writes the value that reaches the setter straight into the
/// AngleSharp DOM, so the DOM and the render tree never diverge in the first place. Resync itself is
/// covered by <c>bind-resync.spec.ts</c> in <c>BlazorCodeFirst.WebAppTests/browser</c>, against a real
/// browser, over <c>BindResyncView</c> in <c>BlazorCodeFirst.WebAppTestHost</c>.
/// </summary>
public partial class NormalizingTextInput : BodyComponentBase
{
    public string Name { get; set; } = "";

    protected override View Body =>
        Html.Input.Type("text").Bind("value", "oninput", () => Name, v => Name = v.Trim());
}

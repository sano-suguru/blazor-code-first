using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The explicit-setter path (Task 5): the setter trims the incoming value. The divergence between the
/// typed text and the normalized field that <c>SetUpdatesAttributeName</c> resyncs is not reachable
/// from bUnit (measured — see the report); resync itself is covered in the browser suite (Task 10).
/// </summary>
public partial class NormalizingTextInput : BodyComponentBase
{
    public string Name { get; set; } = "";

    protected override View Body =>
        Html.Input.Type("text").Bind("value", "oninput", () => Name, v => Name = v.Trim());
}

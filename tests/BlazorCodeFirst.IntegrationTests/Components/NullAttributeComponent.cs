using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The three states an attribute value can be in, through real rendering: a value, the empty string, and
/// null. The button flips all four spellings at once so a re-render is measured and not only the first
/// pass — null is what the surface promises removes the attribute, and a first render alone cannot show a
/// removal.
/// </summary>
public partial class NullAttributeComponent : BodyComponentBase
{
    private bool _present = true;

    protected override View Body =>
        Div[
            Span.Attr("title", _present ? "tip" : null)["to-null"],
            Span.Attr("title", _present ? "tip" : "")["to-empty"],
            Span.Class("card").Class(_present ? "active" : null)["class-join"],
            Span.Class(_present ? "active" : null)["class-single"],
            Button.OnClick(() => _present = !_present)["toggle"]];
}

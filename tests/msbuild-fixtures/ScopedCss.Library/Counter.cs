using BlazorCodeFirst;

namespace Fixtures.ScopedCss.Library;

public partial class Counter : BodyComponentBase
{
    protected override View Body =>
        Html.Fragment(
            Html.Div.OnClick(() => { }),
            Html.Span["hello"]);
}

using BlazorCodeFirst;

namespace Fixtures.ScopedCss;

public partial class Counter : BodyComponentBase
{
    protected override View Body =>
        Html.Fragment(
            Html.Div.OnClick(() => { }),
            Html.Span["hello"]);
}

using BlazorCodeFirst;

namespace Fixtures.ScopedCss.MixedLibrary;

public partial class Counter : BodyComponentBase
{
    protected override View Body =>
        Html.Fragment(
            Html.Div.OnClick(() => { }),
            Html.Span["hello"]);
}

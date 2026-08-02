using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

public partial class CounterComponent : BodyComponentBase
{
    private int _count;

    protected override View Body =>
        Div[
            Span[$"Count: {_count}"],
            Button.OnClick(() => _count++)["Increment"]];
}

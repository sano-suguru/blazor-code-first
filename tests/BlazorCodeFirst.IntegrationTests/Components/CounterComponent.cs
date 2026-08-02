using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

public partial class CounterComponent : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        Div[
            Span[$"Count: {_count}"],
            Button.OnClick(() => _count++)["Increment"]];
}

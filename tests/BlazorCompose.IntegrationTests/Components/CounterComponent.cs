using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class CounterComponent : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        Html.Div(
            Html.Span($"Count: {_count}"),
            Html.Button("Increment").OnClick(() => _count++));
}

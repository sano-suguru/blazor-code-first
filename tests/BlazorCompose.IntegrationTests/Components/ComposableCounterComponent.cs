using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ComposableCounterComponent : ComposeComponentBase
{
    private int _count;
    private int _argumentEvaluations;

    public int ArgumentEvaluations => _argumentEvaluations;

    protected override View Body =>
        Html.Div(
            CounterLabel(GetCountLabel()),
            Html.Button("Increment").OnClick(() => _count++));

    [Composable]
    private static View CounterLabel(string value) => Html.Span(value);

    private string GetCountLabel()
    {
        _argumentEvaluations++;
        return $"Count: {_count}";
    }
}

using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

public partial class ComposableCounterComponent : ComposeComponentBase
{
    private int _count;
    private int _argumentEvaluations;

    public int ArgumentEvaluations => _argumentEvaluations;

    protected override View Body =>
        Div[
            CounterLabel(GetCountLabel()),
            Button.OnClick(() => _count++)["Increment"]];

    [Composable]
    private static View CounterLabel(string value) => Span[value];

    private string GetCountLabel()
    {
        _argumentEvaluations++;
        return $"Count: {_count}";
    }
}

using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

public partial class ViewPartCounterComponent : BodyComponentBase
{
    private int _count;
    private int _argumentEvaluations;

    public int ArgumentEvaluations => _argumentEvaluations;

    protected override View Body =>
        Div[
            CounterLabel(GetCountLabel()),
            Button.OnClick(() => _count++)["Increment"]];

    [ViewPart]
    private static View CounterLabel(string value) => Span[value];

    private string GetCountLabel()
    {
        _argumentEvaluations++;
        return $"Count: {_count}";
    }
}

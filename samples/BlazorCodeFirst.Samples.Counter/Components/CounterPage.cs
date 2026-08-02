using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Samples.Counter.Components;

[Route("/counter")]
public partial class CounterPage : BodyComponentBase
{
    // Stable identity keys (not indices) so the generator can diff the list safely.
    private static readonly List<IncrementStep> Steps = [new(1, 1), new(2, 5), new(3, 10)];

    private int _count;

    protected override View Body =>
        Div.Class("counter")[
            Span[$"Count: {_count}"],
            If(_count >= 3, () => Span["Milestone reached"]),
            Button.OnClick(() => _count++)["Increment"],
            ForEach(
                Steps,
                key: step => step.Id,
                content: step => Button.OnClick(() => _count += step.Amount)[$"+{step.Amount}"])];

    private sealed record IncrementStep(int Id, int Amount);
}

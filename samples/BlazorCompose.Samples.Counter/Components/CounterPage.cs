using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

namespace BlazorCompose.Samples.Counter.Components;

[Route("/counter")]
public partial class CounterPage : ComposeComponentBase
{
    // Stable identity keys (not indices) so the generator can diff the list safely.
    private static readonly List<IncrementStep> Steps = [new(1, 1), new(2, 5), new(3, 10)];

    private int _count;

    protected override View Body =>
        Div(
            Span($"Count: {_count}"),
            If(_count >= 3, () => Span("Milestone reached")),
            Button("Increment").OnClick(() => _count++),
            ForEach(Steps, key: step => step.Id, content: step => Button($"+{step.Amount}").OnClick(() => _count += step.Amount)))
        .Class("bc-counter");

    private sealed record IncrementStep(int Id, int Amount);
}

using Microsoft.AspNetCore.Components;
using BlazorCompose;

namespace BlazorCompose.Site.Pages;

[Route("/counter")]
public partial class CounterPage : ComposeComponentBase
{
    // Stable identity keys (not indices) so the generator can diff the list safely.
    private static readonly List<IncrementStep> Steps = [new(1, 1), new(2, 5), new(3, 10)];

    private int _count;

    protected override View Body =>
        Html.Div(
            Html.Span($"Count: {_count}"),
            Html.If(_count >= 3, () => Html.Span("Milestone reached")),
            Html.Button("Increment").OnClick(() => _count++),
            Html.ForEach(Steps, key: step => step.Id, content: step => Html.Button($"+{step.Amount}").OnClick(() => _count += step.Amount)));

    private sealed record IncrementStep(int Id, int Amount);
}

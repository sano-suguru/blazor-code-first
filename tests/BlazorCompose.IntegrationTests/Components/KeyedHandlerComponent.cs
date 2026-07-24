using System.Collections.Generic;
using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class KeyedHandlerComponent : ComposeComponentBase
{
    private int _total;
    private readonly List<Step> _steps = [new(1, 1), new(2, 5), new(3, 10)];

    protected override View Body =>
        Html.Div(
            Html.Span($"Total: {_total}"),
            Html.ForEach(_steps, key: s => s.Id, content: s => Html.Button($"+{s.Amount}").OnClick(() => _total += s.Amount)));

    private sealed record Step(int Id, int Amount);
}

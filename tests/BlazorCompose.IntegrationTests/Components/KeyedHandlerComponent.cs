using System.Collections.Generic;
using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

public partial class KeyedHandlerComponent : ComposeComponentBase
{
    private int _total;
    private readonly List<Step> _steps = [new(1, 1), new(2, 5), new(3, 10)];

    protected override View Body =>
        Div[
            Span[$"Total: {_total}"],
            ForEach(_steps, key: s => s.Id, content: s => Button.OnClick(() => _total += s.Amount)[$"+{s.Amount}"])];

    private sealed record Step(int Id, int Amount);
}

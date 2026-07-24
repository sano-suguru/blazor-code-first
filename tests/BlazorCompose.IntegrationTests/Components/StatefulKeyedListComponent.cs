using System.Collections.Generic;
using BlazorCompose;

namespace BlazorCompose.IntegrationTests.Components;

public partial class StatefulKeyedListComponent : ComposeComponentBase
{
    private readonly List<Row> _items = [new(1, "a"), new(2, "b"), new(3, "c")];

    protected override View Body =>
        Html.Div(
            Html.ForEach(_items,
                key: i => i.Id,
                content: i => Html.Component<StatefulRowComponent>().Param(r => r.Label, i.Label)),
            Html.Button("Rotate").OnClick(Rotate));

    private void Rotate()
    {
        var first = _items[0];
        _items.RemoveAt(0);
        _items.Add(first);
    }

    private sealed record Row(int Id, string Label);
}

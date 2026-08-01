using System.Collections.Generic;
using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

public partial class KeyedListComponent : ComposeComponentBase
{
    private readonly List<Row> _items = [new(1, "one"), new(2, "two"), new(3, "three")];

    protected override View Body =>
        Div[
            ForEach(_items, key: r => r.Id, content: r => Span[r.Label]),
            Button.OnClick(Rotate)["Rotate"]];

    private void Rotate()
    {
        var first = _items[0];
        _items.RemoveAt(0);
        _items.Add(first);
    }

    private sealed record Row(int Id, string Label);
}

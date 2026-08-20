using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

public partial class PositionKeyedListComponent : BodyComponentBase
{
    private readonly List<Row> _items = [new(1, "a"), new(2, "b"), new(3, "c")];

    // key = current list position (index): a unique-but-positional key. State sticks to the DOM slot, not
    // the item, the index-key failure mode. Distinct from the identity-keyed positive control.
    protected override View Body =>
        Div[
            ForEach(_items,
                key: i => _items.IndexOf(i),
                content: i => Component<StatefulRowComponent>().Param(r => r.Label, i.Label)),
            Button.OnClick(Rotate)["Rotate"]];

    private void Rotate()
    {
        var first = _items[0];
        _items.RemoveAt(0);
        _items.Add(first);
    }

    private sealed record Row(int Id, string Label);
}

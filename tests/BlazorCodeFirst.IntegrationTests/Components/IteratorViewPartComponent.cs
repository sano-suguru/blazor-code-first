using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// A keyed list rendered through the iterator <c>[ViewPart]</c> splice (#316), not through
/// <c>ForEach</c> written directly at the call site. Mirrors <see cref="StatefulKeyedListComponent"/>'s
/// shape -- same stateful row, same key = item identity -- so the rendering proof already established for
/// <c>ForEach</c> also covers the path this feature adds: <c>Rows</c> is a leading-statements-then-one-
/// braced-foreach-ending-in-one-yield-return iterator, spliced into its caller with
/// <c>Div[[.. Rows(_items), …]]</c>, and the emitted key sits on the yielded element itself rather than
/// being threaded through a combinator argument.
/// </summary>
public partial class IteratorViewPartComponent : BodyComponentBase
{
    private readonly List<Row> _items = [new(1, "a"), new(2, "b"), new(3, "c")];
    private int _nextId = 4;

    protected override View Body =>
        Div[[
            .. Rows(_items),
            Button.OnClick(Rotate)["Rotate"],
            Button.OnClick(InsertAtFront)["Insert"],
            Button.OnClick(RemoveSecond)["Remove"],
        ]];

    [ViewPart]
    private static IEnumerable<View> Rows(IReadOnlyList<Row> items)
    {
        foreach (var item in items)
        {
            yield return Component<StatefulRowComponent>().Key(item.Id).Param(r => r.Label, item.Label);
        }
    }

    private void Rotate()
    {
        var first = _items[0];
        _items.RemoveAt(0);
        _items.Add(first);
    }

    private void InsertAtFront() => _items.Insert(0, new Row(_nextId++, "z"));

    private void RemoveSecond() => _items.RemoveAt(1);

    private sealed record Row(int Id, string Label);
}

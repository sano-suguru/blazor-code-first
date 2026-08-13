using System.Collections.Generic;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The third control beside <see cref="StatefulKeyedListComponent"/> and
/// <see cref="PositionKeyedListComponent"/>: no key at all (#172).
/// </summary>
/// <remarks>
/// This is what backs <c>DESIGN.md</c> §4.2's claim that declining the key diffs as an index-derived key
/// does. The claim is about Blazor's behaviour, not about the generator's output, so no compiler test can
/// hold it: what the generator emits is a <c>foreach</c> without <c>SetKey</c>, and whether that costs
/// per-row state is the renderer's answer. Written to be the position-keyed component with the key
/// removed and nothing else changed.
/// </remarks>
public partial class DeclinedKeyListComponent : BodyComponentBase
{
    private readonly List<Row> _items = [new(1, "a"), new(2, "b"), new(3, "c")];

    protected override View Body =>
        Div[
            ForEach(_items,
                key: null,
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

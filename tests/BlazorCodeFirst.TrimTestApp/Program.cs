using System.Collections.Generic;
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

var component = new TrimCounter();
component.RenderForTrimTest(new RenderTreeBuilder());

public partial class TrimCounter : BodyComponentBase
{
    private int _count;
    private readonly List<Row> _rows = [new Row(1, "First")];

    protected override View Body =>
        Div[
            CountLabel($"Count: {_count}"),
            Button.OnClick(() => _count++)["Increment"],
            ForEach(_rows, key: r => r.Id, content: r => Component<DummyRow>().Param(c => c.Text, r.Label))];

    [Composable]
    private static View CountLabel(string value) => Span[value];

    public void RenderForTrimTest(RenderTreeBuilder builder)
        => BuildRenderTree(builder);

    private sealed record Row(int Id, string Label);
}

public sealed class DummyRow : ComponentBase
{
    [Parameter] public string Text { get; set; } = "";
}

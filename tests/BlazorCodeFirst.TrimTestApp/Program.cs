using System.Collections.Generic;
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

var component = new TrimCounter();
component.RenderForTrimTest(new RenderTreeBuilder());

var layout = new TrimLayout();
layout.RenderForTrimTest(new RenderTreeBuilder());

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

// A layout exercises the same trimming contract as a component: Chrome is the inert design-time
// getter, RenderView is what the generator emits and what BuildRenderTree roots. Rendering it here
// keeps the generated code reachable so the trimmer's removal of Chrome is a real result rather
// than the whole type being dropped.
public partial class TrimLayout : ChromeLayoutBase
{
    [Parameter] public string Title { get; set; } = "";

    protected override View Chrome =>
        Div.Class("shell")[
            Header[ChromeTitle(Title)],
            Main[Body]];

    [Composable]
    private static View ChromeTitle(string value) => Span[value];

    public void RenderForTrimTest(RenderTreeBuilder builder)
        => BuildRenderTree(builder);
}

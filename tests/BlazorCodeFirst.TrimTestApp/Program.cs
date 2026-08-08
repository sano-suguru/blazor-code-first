using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

var component = new TrimCounter();
component.RenderForTrimTest(new RenderTreeBuilder());

var layout = new TrimLayout();
layout.RenderForTrimTest(new RenderTreeBuilder());

public partial class TrimCounter : BodyComponentBase
{
    private int _count;
    private string _name = "";
    private bool _agreed;
    private readonly List<Row> _rows = [new Row(1, "First")];

    // Exercises every Bind overload at least once: all six on the element surface (string and bool,
    // each getter-only, explicit setter, and explicit async setter) and all three on the component
    // surface (ComponentView<T>.Bind). All are design-time-only, read by the source generator and never
    // invoked at runtime, so the trim tests assert none of their MethodDefs survive publishing — and
    // those assertions only mean anything for an overload that is called here, since an overload no
    // call site reaches would be trimmed as dead code whether or not the generator folded it away.
    // Both ComponentView<T>.Template overloads are reached on the same terms.
    protected override View Body =>
        Div[
            CountLabel($"Count: {_count}"),
            Button.OnClick(() => _count++)["Increment"],
            Input.Type("text").Bind("value", "oninput", () => _name),
            Input.Type("text").Bind("value", "oninput", () => _name, v => _name = v),
            Input.Type("text").Bind("value", "oninput", () => _name, async v =>
            {
                _name = v;
                await Task.Yield();
            }),
            Input.Type("checkbox").Bind("checked", "onchange", () => _agreed),
            Input.Type("checkbox").Bind("checked", "onchange", () => _agreed, v => _agreed = v),
            Input.Type("checkbox").Bind("checked", "onchange", () => _agreed, async v =>
            {
                _agreed = v;
                await Task.Yield();
            }),
            Component<InputText>().Bind(c => c.Value, () => _name),
            // InputText.Value is string?, so TValue infers nullable and the setters coalesce.
            Component<InputText>().Bind(c => c.Value, () => _name, v => _name = v ?? ""),
            Component<InputText>().Bind(c => c.Value, () => _name, async v =>
            {
                _name = v ?? "";
                await Task.Yield();
            }),
            Component<TrimTemplate>().Template(c => c.RowTemplate, Span["ignored context"]),
            Component<TrimTemplate>().Template(c => c.RowTemplate, row => Span[$"Row {row}"]),
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

// The target of both ComponentView<T>.Template overloads. Hand-written in C# rather than declared as a
// .razor component because a source generator cannot observe another generator's output, so a
// same-project Razor type could not be named as the type argument at all (BCF3012, #145).
public sealed class TrimTemplate : ComponentBase
{
    [Parameter] public RenderFragment<int>? RowTemplate { get; set; }
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

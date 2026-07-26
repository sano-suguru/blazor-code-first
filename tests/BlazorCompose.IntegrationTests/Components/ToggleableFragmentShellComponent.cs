using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

// Places a conditional/nullable RenderFragment position directly next to a stateful sibling
// component inside ONE Compose-generated Body, so toggling the fragment exercises the generator's
// own static sequence allocation for that position. This is deliberately unlike hosting the
// fragment and the sibling in two independently-diffed component instances: Blazor already treats
// separate components as opaque frames isolated from each other, so that shape cannot exercise
// (or break) the generator's sequence numbering — only a single shared render tree can.
public partial class ToggleableFragmentShellComponent : ComposeComponentBase
{
    private static readonly RenderFragment Slot = builder => builder.AddContent(0, "kid");

    private bool _show = true;

    protected override View Body =>
        Div(
            _show ? Slot : null,
            Component<StatefulRowComponent>().Param(r => r.Label, "sibling"),
            Button("Toggle").OnClick(Toggle));

    private void Toggle() => _show = !_show;
}

using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// Places a conditional/nullable RenderFragment position directly next to a stateful sibling
// component inside ONE BlazorCodeFirst-generated Body, so toggling the fragment exercises the generator's
// own static sequence allocation for that position. This is deliberately unlike hosting the
// fragment and the sibling in two independently-diffed component instances: Blazor already treats
// separate components as opaque frames isolated from each other, so that shape cannot exercise
// (or break) the generator's sequence numbering — only a single shared render tree can.
public partial class ToggleableFragmentShellComponent : BodyComponentBase
{
    private static readonly RenderFragment Slot = builder => builder.AddContent(0, "kid");

    private bool _show = true;

    protected override View Body =>
        Div[
            _show ? Slot : null,
            Component<StatefulRowComponent>().Param(r => r.Label, "sibling"),
            Button.OnClick(Toggle)["Toggle"]];

    private void Toggle() => _show = !_show;
}

using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// Draws chrome around the routed page (Body), a plain [Parameter] (Label), and an unnamed
// [CascadingParameter] (Theme) simultaneously, so the integration tests can prove all three arrive
// together through real Blazor parameter binding.
public partial class ProbeLayout : ComposeLayoutBase
{
    [CascadingParameter]
    public string? Theme { get; set; }

    [Parameter]
    public string Label { get; set; } = "";

    protected override View Chrome =>
        Div.Class("shell")[
            Header[Span.Class("label")[Label], Span.Class("theme")[Theme ?? "no-theme"]],
            Main.Class("page")[Body]];
}

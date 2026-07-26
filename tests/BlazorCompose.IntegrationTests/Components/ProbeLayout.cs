using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

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
        Div(
            Header(Span(Label).Class("label"), Span(Theme ?? "no-theme").Class("theme")),
            Main(Body).Class("page"))
        .Class("shell");
}

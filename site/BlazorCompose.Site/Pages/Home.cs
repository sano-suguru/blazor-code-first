using Microsoft.AspNetCore.Components;
using BlazorCompose;

namespace BlazorCompose.Site.Pages;

[Route("/")]
public partial class Home : ComposeComponentBase
{
    protected override View Body =>
        Html.Div(
            Html.Span("BlazorCompose docs site — WASM feasibility spike"),
            Html.Span("Navigate to /counter to exercise events, If, and keyed ForEach."));
}

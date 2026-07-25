using Microsoft.AspNetCore.Components;
using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.Site.Pages;

[Route("/")]
public partial class Home : ComposeComponentBase
{
    protected override View Body =>
        Div(
            Span("BlazorCompose docs site — WASM feasibility spike"),
            Span("Navigate to /counter to exercise events, If, and keyed ForEach."));
}

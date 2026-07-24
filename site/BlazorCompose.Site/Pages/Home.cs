using Microsoft.AspNetCore.Components;
using static BlazorCompose.UI;

namespace BlazorCompose.Site.Pages;

[Route("/")]
public partial class Home : ComposeComponentBase
{
    protected override View Body =>
        VStack(
            Text("BlazorCompose docs site — WASM feasibility spike"),
            Text("Navigate to /counter to exercise events, If, and keyed ForEach."));
}

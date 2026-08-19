using System.Collections.Generic;
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// A Compose-authored component declaring CaptureUnmatchedValues, forwarding what it captures onto
// its own root element via .Attrs — the receiving half #387 unblocks.
public partial class SplatButton : BodyComponentBase
{
    [Parameter]
    public string? Label { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override View Body =>
        Button.Attrs(AdditionalAttributes).Class("btn")[Label];
}

// A Compose call site handing a component call a constant attribute (#314's sending half), which
// Blazor routes into SplatButton's CaptureUnmatchedValues since SplatButton declares no "class"
// parameter of its own.
public partial class SplatButtonHost : BodyComponentBase
{
    protected override View Body =>
        Component<SplatButton>()
            .Class("primary")
            .Attr("data-testid", "host-button")
            .Param(c => c.Label, "Click me");
}

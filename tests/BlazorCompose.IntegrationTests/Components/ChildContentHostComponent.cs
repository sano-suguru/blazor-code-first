using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

// Exercises the implicit View(RenderFragment?) conversion: ChildContent flows straight into Div(...)
// and lowers to AddContent(seq, RenderFragment?), including the null case (no frame emitted at all).
public partial class ChildContentHostComponent : ComposeComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override View Body => Div(ChildContent).Class("card");
}

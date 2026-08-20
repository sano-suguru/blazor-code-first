using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// Exercises the implicit View(RenderFragment?) conversion: ChildContent flows straight into Div[...]
// and lowers to AddContent(seq, RenderFragment?), including the null case (no frame emitted at all).
public partial class ChildContentHostComponent : BodyComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override View Body => Div.Class("card")[ChildContent];
}

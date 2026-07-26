using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

// Routable page declaring its layout the same way a real app page would, so RouteView can resolve
// ProbeLayout via reflection over this [Layout] attribute (Microsoft.AspNetCore.Components.RouteView
// does exactly this, independent of anything BlazorCompose-specific).
[Layout(typeof(ProbeLayout))]
public partial class LayoutProbePage : ComposeComponentBase
{
    protected override View Body => P("page content").Class("page-content");
}

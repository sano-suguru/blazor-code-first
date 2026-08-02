using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// Routable page declaring its layout the same way a real app page would, so RouteView can resolve
// ProbeLayout via reflection over this [Layout] attribute (Microsoft.AspNetCore.Components.RouteView
// does exactly this, independent of anything BlazorCodeFirst-specific).
[Layout(typeof(ProbeLayout))]
public partial class LayoutProbePage : ComposeComponentBase
{
    protected override View Body => P.Class("page-content")["page content"];
}

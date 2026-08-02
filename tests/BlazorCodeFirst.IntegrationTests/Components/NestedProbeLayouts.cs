using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// A two-level layout chain. Nesting is resolved entirely by Blazor: LayoutView reads the [Layout]
// attribute on the layout TYPE, and a Compose layout is an ordinary LayoutComponentBase descendant
// with an ordinary Body parameter, so no BlazorCodeFirst machinery is involved. That reasoning was
// checked during the #33 review but never exercised.
public partial class OuterProbeLayout : ComposeLayoutBase
{
    protected override View Chrome => Div.Class("outer")[Main.Class("outer-slot")[Body]];
}

[Layout(typeof(OuterProbeLayout))]
public partial class InnerProbeLayout : ComposeLayoutBase
{
    protected override View Chrome => Div.Class("inner")[Main.Class("inner-slot")[Body]];
}

[Layout(typeof(InnerProbeLayout))]
public partial class NestedLayoutProbePage : ComposeComponentBase
{
    protected override View Body => P.Class("nested-page-content")["nested page content"];
}

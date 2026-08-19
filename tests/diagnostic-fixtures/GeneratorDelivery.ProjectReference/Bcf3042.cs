using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3042: <c>.Attr</c> on a component call names, case-insensitively, a declared <c>[Parameter]</c>
/// (<c>Widget.Label</c>). Blazor's own parameter binding matches names case-insensitively, so
/// <c>"label"</c> would otherwise silently set <c>Label</c> at runtime, bypassing <c>.Param</c>'s type
/// checking entirely.
/// </summary>
public partial class Bcf3042Host : BodyComponentBase
{
    protected override View Body => Component<Widget>().Attr("label", "hi");
}

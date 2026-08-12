using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF2001 (info): the call cannot be expanded statically, so the area renders through the fragment the
/// returned <c>View</c> carries and loses its static diff optimization.
/// </summary>
public partial class Bcf2001Host : BodyComponentBase
{
    protected override View Body => Div[Wrap()];

    private static View Wrap()
    {
        RenderFragment fragment = builder => builder.AddContent(0, "bcf2001");
        return fragment;
    }
}

/// <summary>
/// BCF3030: the callee builds its <c>View</c> from the design-time surface but carries no
/// <c>[ViewPart]</c>, so the call renders nothing.
/// </summary>
public partial class Bcf3030Host : BodyComponentBase
{
    protected override View Body => Div[Card("bcf3030")];

    private static View Card(string title) => Span[title];
}

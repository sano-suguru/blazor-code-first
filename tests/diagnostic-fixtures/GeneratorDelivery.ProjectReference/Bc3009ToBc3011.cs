using BlazorCompose;
using static BlazorCompose.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BC3009: an empty tag. Its position is pinned by the tests, not just its id: BC3009 and BC3010 are
/// the two diagnostics whose precise <c>file(line,col)</c> is asserted end-to-end, so a regression to
/// a location-less diagnostic (#77) is caught here rather than rediscovered.
/// </summary>
public partial class Bc3009Host : ComposeComponentBase
{
    protected override View Body => Element("")[Span["bc3009"]];
}

/// <summary>BC3010: the same attribute bound twice on one element.</summary>
public partial class Bc3010Host : ComposeComponentBase
{
    protected override View Body => Div.Id("first").Id("second")[Span["bc3010"]];
}

/// <summary>BC3011: an attribute name that is not a compile-time constant.</summary>
public partial class Bc3011Host : ComposeComponentBase
{
    private readonly string _name = "data-bc3011";

    protected override View Body => Div.Attr(_name, "value")[Span["bc3011"]];
}

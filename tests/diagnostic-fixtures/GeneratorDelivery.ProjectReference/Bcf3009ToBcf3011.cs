using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3009: an empty tag. Its position is pinned by the tests, not just its id: BCF3009 and BCF3010 are
/// the two diagnostics whose precise <c>file(line,col)</c> is asserted end-to-end, so a regression to
/// a location-less diagnostic (#77) is caught here rather than rediscovered.
/// </summary>
public partial class Bcf3009Host : BodyComponentBase
{
    protected override View Body => Element("")[Span["bcf3009"]];
}

/// <summary>BCF3010: the same attribute bound twice on one element.</summary>
public partial class Bcf3010Host : BodyComponentBase
{
    protected override View Body => Div.Id("first").Id("second")[Span["bcf3010"]];
}

/// <summary>BCF3011: an attribute name that is not a compile-time constant.</summary>
public partial class Bcf3011Host : BodyComponentBase
{
    private readonly string _name = "data-bcf3011";

    protected override View Body => Div.Attr(_name, "value")[Span["bcf3011"]];
}

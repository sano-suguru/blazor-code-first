using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BCF1002: a <c>[ViewPart]</c> method that is not static.</summary>
public partial class Bcf1002Host : BodyComponentBase
{
    protected override View Body => Span["bcf1002"];

    [ViewPart]
    private View Helper() => Span["helper"];
}

/// <summary>
/// BCF1003: the design-time expression reads a stored <c>View</c>, which is neither design-time syntax
/// nor a call and so cannot be classified. Wrapped in translatable element helpers on purpose: the
/// reported location must be the offending expression, not the whole <c>Body</c> (#77).
/// </summary>
/// <remarks>
/// A <c>View</c>-returning call used to be the shape here. It has a route of its own now: BCF3030 when
/// the callee builds from the design-time surface, BCF2001 otherwise.
/// </remarks>
public partial class Bcf1003Host : BodyComponentBase
{
    private readonly View _cached;

    protected override View Body => Div[Span["bcf1003"], _cached];
}

/// <summary>BCF1004: a getter that does not reduce to a single expression.</summary>
public partial class Bcf1004Host : BodyComponentBase
{
    protected override View Body
    {
        get
        {
            var label = "bcf1004";
            return Span[label];
        }
    }
}

using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BCF1002: a <c>[Composable]</c> method that is not static.</summary>
public partial class Bcf1002Host : BodyComponentBase
{
    protected override View Body => Span["bcf1002"];

    [Composable]
    private View Helper() => Span["helper"];
}

/// <summary>
/// BCF1003: the design-time expression calls a plain method returning <c>View</c>, which is not
/// statically analyzable and has no runtime fallback yet.  Wrapped in translatable element helpers on
/// purpose: the reported location must be the offending call, not the whole <c>Body</c> (#77).
/// </summary>
public partial class Bcf1003Host : BodyComponentBase
{
    protected override View Body => Div[Span["bcf1003"], Make()];

    private static View Make() => Span["bcf1003"];
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

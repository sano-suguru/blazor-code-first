using BlazorCompose;
using static BlazorCompose.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BC1002: a <c>[Composable]</c> method that is not static.</summary>
public partial class Bc1002Host : ComposeComponentBase
{
    protected override View Body => Span["bc1002"];

    [Composable]
    private View Helper() => Span["helper"];
}

/// <summary>
/// BC1003: the design-time expression calls a plain method returning <c>View</c>, which is not
/// statically analyzable and has no runtime fallback yet.  Wrapped in translatable element helpers on
/// purpose: the reported location must be the offending call, not the whole <c>Body</c> (#77).
/// </summary>
public partial class Bc1003Host : ComposeComponentBase
{
    protected override View Body => Div[Span["bc1003"], Make()];

    private static View Make() => Span["bc1003"];
}

/// <summary>BC1004: a getter that does not reduce to a single expression.</summary>
public partial class Bc1004Host : ComposeComponentBase
{
    protected override View Body
    {
        get
        {
            var label = "bc1004";
            return Span[label];
        }
    }
}

using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3033: the same non-attribute frame decoration written twice on one node. <c>.Key</c> is the channel
/// used here because it is the one every receiver declares; the other channels of the rule are covered
/// in-process, and a real build only needs one of them to prove the rule is reachable.
/// </summary>
public partial class Bcf3033Host : BodyComponentBase
{
    private readonly int _id = 1;

    protected override View Body =>
        Div.Key(_id).Key(_id + 1)["keyed twice"];
}

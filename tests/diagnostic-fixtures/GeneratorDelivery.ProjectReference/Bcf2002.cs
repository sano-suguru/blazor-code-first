using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF2002 (info): a native `if`/`else` transplanted into a Body getter degrades to a dynamic
/// region, so each arm's content renders through a runtime fragment and loses its static diff
/// optimization.
/// </summary>
public partial class Bcf2002Host : BodyComponentBase
{
    private readonly bool _flag = true;

    protected override View Body
    {
        get
        {
            if (_flag)
            {
                return Span["bcf2002"];
            }
            else
            {
                return Span["fallback"];
            }
        }
    }
}

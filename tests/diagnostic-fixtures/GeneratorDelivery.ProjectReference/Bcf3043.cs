using System.Collections.Generic;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3043: a loop's source argument is a call to a <c>[ViewPart]</c>. The callee's body is built
/// from the design-time surface, so it renders nothing when run as ordinary code at the source
/// position instead of being statically expanded.
/// </summary>
public partial class Bcf3043Host : BodyComponentBase
{
    private readonly List<string> _items = ["a", "b"];

    [ViewPart]
    private static IEnumerable<View> Rows(List<string> items)
    {
        foreach (var item in items)
        {
            yield return Li[item];
        }
    }

    protected override View Body =>
        ForEach(Rows(_items), item => 0, item => Span["x"]);
}

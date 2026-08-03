using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF1001: declares the design-time expression without <c>partial</c>. This is the shape from issue
/// #76, no generated RenderView means CS0534, a declaration-level error, which is exactly why this
/// diagnostic cannot be analyzer-reported.
/// </summary>
public class Bcf1001NonPartial : BodyComponentBase
{
    protected override View Body => Span["bcf1001"];
}

/// <summary>BCF1005: a nested type cannot be generated into.</summary>
public partial class Bcf1005Outer
{
    public partial class Nested : BodyComponentBase
    {
        protected override View Body => Span["bcf1005"];
    }
}

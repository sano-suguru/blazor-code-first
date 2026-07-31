using BlazorCompose;
using static BlazorCompose.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BC1001: declares the design-time expression without <c>partial</c>. This is the shape from issue
/// #76 — no generated RenderView means CS0534, a declaration-level error, which is exactly why this
/// diagnostic cannot be analyzer-reported.
/// </summary>
public class Bc1001NonPartial : ComposeComponentBase
{
    protected override View Body => Span("bc1001");
}

/// <summary>BC1005: a nested type cannot be generated into.</summary>
public partial class Bc1005Outer
{
    public partial class Nested : ComposeComponentBase
    {
        protected override View Body => Span("bc1005");
    }
}

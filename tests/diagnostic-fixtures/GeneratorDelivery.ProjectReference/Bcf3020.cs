using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3020: a parameter bound in both directions on a component that declares no change callback for it.
/// <c>Widget</c> is reused rather than given a purpose-built sibling, because it already has the shape
/// this diagnostic is about: an ordinary <c>[Parameter]</c> with no <c>LabelChanged</c> beside it. That is
/// how the mistake arrives — the call site looks like any other, and the name that is missing is one the
/// author never wrote.
/// </summary>
public partial class Bcf3020Host : BodyComponentBase
{
    private string _label = "bcf3020";

    protected override View Body => Component<Widget>().Bind(w => w.Label, () => _label);
}

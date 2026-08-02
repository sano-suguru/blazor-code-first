using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3012: the component type argument does not resolve while the generator runs. Written here as a
/// name that does not exist at all; the shape the diagnostic is really about — a <c>.razor</c>
/// component in the same project — is invisible to the generator for the same reason and reaches the
/// same code path.
/// </summary>
public partial class Bcf3012Host : ComposeComponentBase
{
    protected override View Body => Component<MissingWidget>();
}

/// <summary>BCF3013: child content given to a component that cannot receive it.</summary>
public partial class Bcf3013Host : ComposeComponentBase
{
    protected override View Body => Component<ChildlessWidget>()[Span["bcf3013"]];
}

/// <summary>BCF3014: an inert design-time value bound as a parameter value.</summary>
public partial class Bcf3014Host : ComposeComponentBase
{
    protected override View Body => Component<Widget>().Param(w => w.Payload, Span["bcf3014"]);
}

/// <summary>BCF3015: an unresolved type reference in a value position, not rooted at global::.</summary>
public partial class Bcf3015Host : ComposeComponentBase
{
    protected override View Body => Component<Widget>().Param(w => w.Kind, typeof(MissingProbe));
}

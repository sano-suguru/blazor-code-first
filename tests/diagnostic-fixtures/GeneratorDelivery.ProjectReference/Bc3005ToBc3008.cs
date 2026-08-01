using BlazorCompose;
using static BlazorCompose.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BC3005: the parameter selector is not a plain property selection.</summary>
public partial class Bc3005Host : ComposeComponentBase
{
    protected override View Body =>
        Component<Widget>().Param(w => w.Label!.ToUpperInvariant(), "bc3005");
}

/// <summary>BC3006: the selected property is not a settable <c>[Parameter]</c>.</summary>
public partial class Bc3006Host : ComposeComponentBase
{
    protected override View Body =>
        Component<Widget>().Param(w => w.NotAParameter, "bc3006");
}

/// <summary>BC3007: the same parameter is bound twice.</summary>
public partial class Bc3007Host : ComposeComponentBase
{
    protected override View Body =>
        Component<Widget>().Param(w => w.Label, "first").Param(w => w.Label, "second");
}

/// <summary>
/// BC3008: a decoration applied to something that is not a single element. The receiver here is a
/// <c>View</c> (from <c>If</c>), which has no <c>.Class</c> to bind, so the generator detects it on the
/// failure path and reports BC3008. The C# error that would otherwise report it (CS1929) cannot carry the
/// constraint in a real build: this class necessarily carries CS0534, and <c>csc</c> stops after the
/// declaration stage without binding method bodies, so the CS1929 is never computed. This fixture is what
/// establishes that BC3008 is delivered where CS1929 is not.
/// </summary>
public partial class Bc3008Host : ComposeComponentBase
{
    protected override View Body => If(true, () => Div["bc3008"]).Class("decorated");
}

using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>BCF3005: the parameter selector is not a plain property selection.</summary>
public partial class Bcf3005Host : ComposeComponentBase
{
    protected override View Body =>
        Component<Widget>().Param(w => w.Label!.ToUpperInvariant(), "bcf3005");
}

/// <summary>BCF3006: the selected property is not a settable <c>[Parameter]</c>.</summary>
public partial class Bcf3006Host : ComposeComponentBase
{
    protected override View Body =>
        Component<Widget>().Param(w => w.NotAParameter, "bcf3006");
}

/// <summary>BCF3007: the same parameter is bound twice.</summary>
public partial class Bcf3007Host : ComposeComponentBase
{
    protected override View Body =>
        Component<Widget>().Param(w => w.Label, "first").Param(w => w.Label, "second");
}

/// <summary>
/// BCF3008: a decoration applied to something that is not a single element. The receiver here is a
/// <c>View</c> (from <c>If</c>), which has no <c>.Class</c> to bind, so the generator detects it on the
/// failure path and reports BCF3008. The C# error that would otherwise report it (CS1929) cannot carry the
/// constraint in a real build: this class necessarily carries CS0534, and <c>csc</c> stops after the
/// declaration stage without binding method bodies, so the CS1929 is never computed. This fixture is what
/// establishes that BCF3008 is delivered where CS1929 is not.
/// </summary>
public partial class Bcf3008Host : ComposeComponentBase
{
    protected override View Body => If(true, () => Div["bcf3008"]).Class("decorated");
}

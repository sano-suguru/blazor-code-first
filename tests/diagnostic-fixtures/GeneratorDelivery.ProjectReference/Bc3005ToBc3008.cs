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

/// <summary>BC3008: a decoration applied to something that is not a single element.</summary>
public partial class Bc3008Host : ComposeComponentBase
{
    protected override View Body => If(true, () => Div("bc3008")).Class("decorated");
}

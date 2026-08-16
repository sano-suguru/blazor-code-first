using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// BCF3034: a call-site render mode on a component whose own declaration fixes one.
/// </summary>
/// <remarks>
/// Both the attribute and the mode are declared here. The framework ships no concrete
/// <see cref="RenderModeAttribute"/> — it is abstract, and Razor's <c>@rendermode</c> directive generates
/// a subclass per component, which a <c>.cs</c> file has no counterpart for. The named modes
/// (<c>RenderMode.InteractiveServer</c> and its siblings) live in
/// <c>Microsoft.AspNetCore.Components.Web</c>, and none of them is reached for here; the rule asks nothing
/// of the mode's identity, only of the attribute on the component type.
/// </remarks>
public sealed class Bcf3034Mode : IComponentRenderMode
{
}

public sealed class Bcf3034InteractiveAttribute : RenderModeAttribute
{
    public override IComponentRenderMode Mode { get; } = new Bcf3034Mode();
}

[Bcf3034Interactive]
public sealed class Bcf3034Fixed : ComponentBase
{
}

public partial class Bcf3034Host : BodyComponentBase
{
    private static readonly Bcf3034Mode Mode = new();

    protected override View Body => Component<Bcf3034Fixed>().RenderMode(Mode);
}

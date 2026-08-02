using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// A plain <see cref="ComponentBase"/> row with internal state (<c>_count</c>) that is not derived from
/// any parameter. Written by hand (no Component&lt;T&gt; / BodyComponentBase) so the state-preservation
/// test does not depend on the feature under test to build its own fixture. Renders its label and count so
/// bUnit can observe the internal state through the DOM.
/// </summary>
public sealed class StatefulRowComponent : ComponentBase
{
    [Parameter] public string Label { get; set; } = "";

    private int _count;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.OpenElement(1, "span");
        builder.AddContent(2, $"{Label}:{_count}");
        builder.CloseElement();
        builder.OpenElement(3, "button");
        builder.AddAttribute(4, "onclick", EventCallback.Factory.Create(this, () => _count++));
        builder.AddContent(5, "+");
        builder.CloseElement();
        builder.CloseElement();
    }
}

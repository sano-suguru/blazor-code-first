using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;

namespace BlazorCodeFirst.IntegrationTests;

public sealed class CaptureUnmatchedValuesRenderingTests : BunitContext
{
    [Fact]
    public void NonCollidingAttributeWrittenAtOneComposeCallSite_ReachesAnotherComposeComponentsRootElement()
    {
        var cut = Render<SplatButtonHost>();

        var button = cut.Find("button");

        // "data-testid" is an attribute SplatButton has no parameter for at all, so Blazor
        // captures it into AdditionalAttributes, and SplatButton's own .Attrs(AdditionalAttributes)
        // forwards it onto <button> untouched — SplatButton's own body never writes a
        // "data-testid" of its own, so nothing collides with it. This is the round trip #314
        // exists for: an attribute crosses two Compose components with no .razor involved.
        Assert.Equal("host-button", button.GetAttribute("data-testid"));

        // An ordinary declared parameter, unaffected by any of this.
        Assert.Equal("Click me", button.TextContent);
    }

    [Fact]
    public void CollidingAttributeWrittenAtTheCallSite_LosesToTheReceivingComponentsOwnExplicitDecoration()
    {
        var cut = Render<SplatButtonHost>();

        var button = cut.Find("button");

        // SplatButtonHost writes .Class("primary") on the Component<SplatButton>() call, which
        // Blazor captures into AdditionalAttributes as {"class": "primary"} (SplatButton has no
        // "class" [Parameter] of its own). SplatButton's own body ALSO writes .Class("btn")
        // explicitly, and splat-first means that explicit decoration is emitted after the splat,
        // so RenderTreeBuilder.CloseElement's last-frame-wins resolution keeps "btn" and drops
        // "primary" outright — RenderTreeBuilder never merges a repeated "class" under any
        // emission order (measured, AttributeSplatMeasurementTests). This is the cost #387's own
        // argument names explicitly: a caller can no longer override an attribute the receiving
        // component's own body already writes.
        Assert.Contains("btn", button.ClassList);
        Assert.DoesNotContain("primary", button.ClassList);
    }
}

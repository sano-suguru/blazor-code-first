using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCompose.IntegrationTests.Components;

/// <summary>
/// Hand-written (not Compose) harness placing <see cref="ChildContentHostComponent"/> next to a
/// stateful sibling (<see cref="StatefulRowComponent"/>) at fixed sequence positions, and toggling the
/// host's ChildContent between a non-null fragment and null on each click. Written directly against
/// <see cref="RenderTreeBuilder"/>, mirroring <see cref="StatefulRowComponent"/>, so the
/// state-preservation test does not depend on the Compose-generated toggling path it is verifying.
/// </summary>
public sealed class ToggleableChildContentComponent : ComponentBase
{
    private bool _showChildContent = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");

        builder.OpenComponent<ChildContentHostComponent>(1);
        builder.AddComponentParameter(
            2,
            nameof(ChildContentHostComponent.ChildContent),
            _showChildContent ? (RenderFragment)(b => b.AddContent(0, "kid")) : null);
        builder.CloseComponent();

        builder.OpenComponent<StatefulRowComponent>(3);
        builder.AddComponentParameter(4, nameof(StatefulRowComponent.Label), "sibling");
        builder.CloseComponent();

        builder.OpenElement(5, "button");
        builder.AddAttribute(6, "onclick", EventCallback.Factory.Create(this, Toggle));
        builder.AddContent(7, "Toggle");
        builder.CloseElement();

        builder.CloseElement();
    }

    private void Toggle() => _showChildContent = !_showChildContent;
}

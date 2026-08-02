using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCompose.IntegrationTests.Components;

// A plain Blazor component with two fragment slots, used as the target of Component<T>()[children]
// and .Param(c => c.Footer, …). Hand-written C# because a same-project .razor type cannot be a
// Component<T> type argument (BCF3012).
public sealed class SlotCardComponent : ComponentBase
{
    [Parameter]
    public string Title { get; set; } = "";

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public RenderFragment? Footer { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "card");
        builder.AddContent(2, Title);
        builder.OpenElement(3, "div");
        builder.AddAttribute(4, "class", "body");
        builder.AddContent(5, ChildContent);
        builder.CloseElement();
        builder.OpenElement(6, "div");
        builder.AddAttribute(7, "class", "foot");
        builder.AddContent(8, Footer);
        builder.CloseElement();
        builder.CloseElement();
    }
}

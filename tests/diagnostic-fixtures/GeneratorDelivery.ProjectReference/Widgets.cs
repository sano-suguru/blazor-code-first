using Microsoft.AspNetCore.Components;

namespace Fixtures.GeneratorDelivery;

/// <summary>Valid Blazor components the broken call sites in this fixture bind against.</summary>
public sealed class Widget : ComponentBase
{
    [Parameter]
    public string? Label { get; set; }

    [Parameter]
    public object? Payload { get; set; }

    [Parameter]
    public Type? Kind { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public string? NotAParameter { get; set; }
}

/// <summary>A component with no <c>ChildContent</c> parameter, for BCF3013.</summary>
public sealed class ChildlessWidget : ComponentBase
{
    [Parameter]
    public string? Label { get; set; }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorCompose.Site.Layout;

/// <summary>
/// Sets the document title from a Compose <c>Body</c>.
/// </summary>
/// <remarks>
/// Written in C# rather than Razor because <c>Component&lt;T&gt;()</c> cannot supply the
/// <c>RenderFragment ChildContent</c> that <c>PageTitle</c> requires. Note that
/// <c>Component&lt;PageTitle&gt;()</c> resolves correctly here — the same-compilation constraint
/// applies only to <c>.razor</c> components declared in this project, not to referenced packages.
/// </remarks>
public sealed class DocTitle : ComponentBase
{
    [Parameter]
    public string Title { get; set; } = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.OpenComponent<PageTitle>(0);
        builder.AddComponentParameter(1, "ChildContent", (RenderFragment)(b => b.AddContent(0, Title)));
        builder.CloseComponent();
    }
}

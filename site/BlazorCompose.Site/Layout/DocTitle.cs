using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorCompose.Site.Layout;

/// <summary>
/// Sets the document title from a Compose <c>Body</c>.
/// </summary>
/// <remarks>
/// Written in C# rather than Razor on purpose: <c>Component&lt;T&gt;()</c> cannot reference a
/// Razor-generated type, because the Razor compiler is itself a source generator and generators
/// cannot see each other's output — the type argument would be an error type when BlazorCompose's
/// generator runs. Wrapping PageTitle at all is necessary because Component&lt;T&gt;() also cannot
/// supply the RenderFragment ChildContent that PageTitle requires.
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

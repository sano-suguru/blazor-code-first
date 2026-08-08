using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace Fixtures.GeneratorDelivery;

/// <summary>
/// A component with a generic fragment parameter, which is what <c>.Template</c> binds. Declared here
/// rather than beside the other widgets because it exists only for this diagnostic.
/// </summary>
public sealed class GenericTemplateWidget : ComponentBase
{
    [Parameter] public RenderFragment<int>? RowTemplate { get; set; }
}

/// <summary>
/// BCF3022: the contextual <c>.Template</c> content is a method group, so there is no inline expression
/// to sequence and no lambda parameter to substitute the generated context variable for.
/// </summary>
public partial class Bcf3022Host : BodyComponentBase
{
    protected override View Body =>
        Component<GenericTemplateWidget>().Template(w => w.RowTemplate, Render);

    private static View Render(int value) => Span[value.ToString()];
}

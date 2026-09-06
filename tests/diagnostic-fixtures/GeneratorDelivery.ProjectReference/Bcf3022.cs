using System;
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
/// BCF3022: the contextual <c>.Template</c> content is a constructed delegate, which names no callee at
/// the call site.
/// </summary>
/// <remarks>
/// A bare method group is no longer this diagnostic's business: it is read as the call it stands for and
/// answered by the same three-way split every other call gets, the same as <c>ForEach</c>'s content
/// (#317). <c>DiagnosticDeliveryTests</c> requires exactly one occurrence of an id across the build, so a
/// fixture holds one shape per diagnostic, and the anchor is matched within a line — which the block shape
/// would not fit on.
/// </remarks>
public partial class Bcf3022Host : BodyComponentBase
{
    protected override View Body =>
        Component<GenericTemplateWidget>().Template(w => w.RowTemplate, new Func<int, View>(Render));

    private static View Render(int value) => Span[value.ToString()];
}

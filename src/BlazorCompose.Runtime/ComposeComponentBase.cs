using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCompose;

/// <summary>
/// Base class for BlazorCompose components. Derived types declare their UI through a design-time
/// <see cref="Body"/> expression that the source generator translates into a <see cref="RenderView"/>
/// override.
/// </summary>
/// <remarks>
/// Components that declare the <see cref="Body"/> override must be declared <c>partial</c> so the
/// generator can emit the <see cref="RenderView"/> implementation into the same class; a non-partial
/// component reports BC1001. The component must also be a top-level type; a nested class is reported
/// as BC1005. A generic component is supported: the generated part repeats the same type parameters.
/// The component otherwise behaves as a standard Blazor <see cref="ComponentBase"/>.
/// </remarks>
public abstract class ComposeComponentBase : ComponentBase
{
    /// <summary>
    /// The design-time-only UI expression describing this component's content.
    /// </summary>
    /// <value>Inert design-time syntax analyzed by the source generator.</value>
    /// <remarks>
    /// <see cref="Body"/> is never evaluated at runtime. It may read component state — that is how a
    /// component projects state to UI — but must not mutate it; state mutation inside it reports BC3001.
    /// The generator analyzes the expression statically and emits the corresponding rendering into
    /// <see cref="RenderView"/>.
    /// </remarks>
    protected abstract View Body { get; }

    /// <summary>
    /// Renders the component's content. The source generator normally emits this method from the
    /// <see cref="Body"/> expression. A component may override it by hand instead, in which case
    /// the generator emits nothing for that type.
    /// </summary>
    /// <param name="builder">The render-tree builder that receives the generated rendering instructions.</param>
    protected abstract void RenderView(RenderTreeBuilder builder);

    /// <summary>Delegates Blazor's render-tree construction to the generator-emitted <see cref="RenderView"/>.</summary>
    /// <param name="builder">The render-tree builder supplied by Blazor.</param>
    protected sealed override void BuildRenderTree(RenderTreeBuilder builder) => RenderView(builder);
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst;

/// <summary>
/// Base class for BlazorCodeFirst layouts. Derived types describe the chrome they draw through a
/// design-time <see cref="Chrome"/> expression and place the routed page with
/// <see cref="LayoutComponentBase.Body"/>.
/// </summary>
/// <remarks>
/// <para>
/// The design-time expression is named <c>Chrome</c> here rather than <c>Body</c> because Blazor
/// requires a layout to expose the routed content under a parameter named exactly <c>Body</c>
/// (<c>LayoutComponentBase.BodyPropertyName</c>, which <c>LayoutView</c> passes by name), and C# cannot
/// declare two members with the same name on one type. So in a layout <c>Body</c> keeps the meaning it
/// has in Razor, the page being wrapped, and <c>Chrome</c> names what the layout itself draws.
/// </para>
/// <para>
/// Deriving from <see cref="LayoutComponentBase"/> is not required by Blazor (any <c>IComponent</c>
/// with a <c>Body</c> parameter works), but it inherits the parameter under the right name together
/// with the <c>[DynamicDependency]</c> trimmer hint on its <c>SetParametersAsync</c>, so no parameter
/// plumbing of our own is needed.
/// </para>
/// <para>
/// Layouts that declare the <see cref="Chrome"/> override must be declared <c>partial</c> so the
/// generator can emit the <see cref="RenderView"/> implementation into the same class; a non-partial
/// layout reports BCF1001. The layout must also be a top-level type; a nested class is reported as
/// BCF1005. A generic layout is supported: the generated part repeats the same type parameters.
/// </para>
/// </remarks>
public abstract class ChromeLayoutBase : LayoutComponentBase
{
    /// <summary>
    /// The design-time-only UI expression describing the chrome this layout draws around
    /// <see cref="LayoutComponentBase.Body"/>.
    /// </summary>
    /// <value>Inert design-time syntax analyzed by the source generator.</value>
    /// <remarks>
    /// <see cref="Chrome"/> is never evaluated at runtime. It may read component state, including
    /// <see cref="LayoutComponentBase.Body"/>, but must not mutate it; state mutation inside it
    /// reports BCF3001.
    /// </remarks>
    protected abstract View Chrome { get; }

    /// <summary>
    /// Renders the layout's content. The source generator normally emits this method from the
    /// <see cref="Chrome"/> expression. A layout may override it by hand instead, in which case
    /// the generator emits nothing for that type.
    /// </summary>
    /// <param name="builder">The render-tree builder that receives the generated rendering instructions.</param>
    protected abstract void RenderView(RenderTreeBuilder builder);

    /// <summary>Delegates Blazor's render-tree construction to the generator-emitted <see cref="RenderView"/>.</summary>
    /// <param name="builder">The render-tree builder supplied by Blazor.</param>
    protected sealed override void BuildRenderTree(RenderTreeBuilder builder) => RenderView(builder);
}

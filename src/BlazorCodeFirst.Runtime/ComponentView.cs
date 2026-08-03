namespace BlazorCodeFirst;

/// <summary>
/// Inert design-time builder for a <see cref="Html.Component{TComponent}()"/> call. The source generator
/// reads the <see cref="Param{TValue}"/> chain statically and emits <c>OpenComponent</c>/
/// <c>AddComponentParameter</c> instructions; instances are never constructed or evaluated at runtime.
/// </summary>
/// <typeparam name="TComponent">The Blazor component type being configured.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "ComponentView<TComponent> is inert design-time syntax with no state to compare; " +
        "it is read by the source generator and never constructed, compared, or persisted at runtime.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1043:Use integral or string argument for indexers",
    Justification = "The indexer is the children channel of a component, not a lookup by index: its argument " +
        "is the component's content, which is a mixed sequence of strings and Views and cannot be expressed " +
        "as an integer or a string. The bracket spelling is the point: it places attributes next to the " +
        "tag, as HTML does, and no integral or string overload could carry it.")]
public readonly struct ComponentView<TComponent>
    where TComponent : Microsoft.AspNetCore.Components.IComponent
{
    /// <summary>Design-time syntax binding a component parameter selected by <paramref name="selector"/>.</summary>
    /// <typeparam name="TValue">The parameter's value type, inferred from the selected property.</typeparam>
    /// <param name="selector">Selects the target parameter property, e.g. <c>c =&gt; c.Items</c>.</param>
    /// <param name="value">The value bound to the selected parameter.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Param<TValue>(System.Func<TComponent, TValue> selector, TValue value) => this;

    /// <summary>
    /// Design-time syntax binding a <see cref="Microsoft.AspNetCore.Components.RenderFragment"/>-typed
    /// parameter to BlazorCodeFirst content, which the generator lowers to a statically sequenced fragment lambda.
    /// </summary>
    /// <param name="selector">Selects the target fragment parameter, e.g. <c>c =&gt; c.ChildContent</c>.</param>
    /// <param name="content">The BlazorCodeFirst content rendered as that parameter's fragment.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    /// <remarks>
    /// Chosen over the generic <see cref="Param{TValue}"/> whenever the value is a <see cref="View"/>,
    /// because <c>RenderFragment?</c> converts to <see cref="View"/>. A real
    /// <c>RenderFragment</c> value still binds through the generic overload and is emitted verbatim.
    /// </remarks>
    public ComponentView<TComponent> Param(
        System.Func<TComponent, Microsoft.AspNetCore.Components.RenderFragment?> selector,
        View content) => this;

    /// <summary>
    /// Design-time syntax binding <paramref name="children"/> to the component's <c>ChildContent</c>
    /// parameter, mirroring how Razor binds nested content.
    /// </summary>
    /// <param name="children">Mixed string and <see cref="View"/> content, in source order.</param>
    /// <returns>The marker <see cref="View"/>; never evaluated at runtime.</returns>
    /// <remarks>
    /// <typeparamref name="TComponent"/> must declare a settable <c>[Parameter]</c> named
    /// <c>ChildContent</c> of type <see cref="Microsoft.AspNetCore.Components.RenderFragment"/>; otherwise
    /// BCF3013 is reported. Use
    /// <see cref="Param(System.Func{TComponent, Microsoft.AspNetCore.Components.RenderFragment?}, View)"/>
    /// for any other fragment-typed parameter. Because this returns <see cref="View"/>, a
    /// <see cref="Param{TValue}"/> call must precede the brackets.
    /// </remarks>
    public View this[params System.ReadOnlySpan<View> children] => default;

    /// <summary>Converts the inert builder to the marker <see cref="View"/> so it composes as a child.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2225:Operator overloads have named alternates",
        Justification = "This mirrors the other Html members that return View directly; a named " +
            "alternate would suggest the conversion does real work, but it is inert design-time syntax " +
            "read by the source generator and always yields the default View at runtime.")]
    public static implicit operator View(ComponentView<TComponent> _) => default;
}

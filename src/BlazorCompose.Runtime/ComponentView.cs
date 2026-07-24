namespace BlazorCompose;

/// <summary>
/// Inert design-time builder for a <see cref="Html.Component{TComponent}"/> call. The source generator
/// reads the <see cref="Param{TValue}"/> chain statically and emits <c>OpenComponent</c>/
/// <c>AddComponentParameter</c> instructions; instances are never constructed or evaluated at runtime.
/// </summary>
/// <typeparam name="TComponent">The Blazor component type being configured.</typeparam>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "ComponentView<TComponent> is inert design-time syntax with no state to compare; " +
        "it is read by the source generator and never constructed, compared, or persisted at runtime.")]
public readonly struct ComponentView<TComponent>
    where TComponent : Microsoft.AspNetCore.Components.IComponent
{
    /// <summary>Design-time syntax binding a component parameter selected by <paramref name="selector"/>.</summary>
    /// <typeparam name="TValue">The parameter's value type, inferred from the selected property.</typeparam>
    /// <param name="selector">Selects the target parameter property, e.g. <c>c =&gt; c.Items</c>.</param>
    /// <param name="value">The value bound to the selected parameter.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Param<TValue>(System.Func<TComponent, TValue> selector, TValue value) => this;

    /// <summary>Converts the inert builder to the marker <see cref="View"/> so it composes as a child.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2225:Operator overloads have named alternates",
        Justification = "This mirrors the other Html factory methods that return View directly; a named " +
            "alternate would suggest the conversion does real work, but it is inert design-time syntax " +
            "read by the source generator and always yields the default View at runtime.")]
    public static implicit operator View(ComponentView<TComponent> _) => default;
}

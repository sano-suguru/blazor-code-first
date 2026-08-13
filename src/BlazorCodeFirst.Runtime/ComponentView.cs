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
    /// Design-time syntax binding a <c>RenderFragment&lt;TContext&gt;</c>-typed parameter to BlazorCodeFirst
    /// content that ignores its context.
    /// </summary>
    /// <typeparam name="TContext">The template context type, inferred from the selected parameter.</typeparam>
    /// <param name="selector">Must name a settable <c>[Parameter]</c> of <c>RenderFragment&lt;TContext&gt;</c>.</param>
    /// <param name="content">The BlazorCodeFirst content rendered for every context; the context is ignored.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    /// <remarks>
    /// A real <c>RenderFragment&lt;TContext&gt;</c> value remains a scalar <see cref="Param{TValue}"/> value.
    /// This method is inert and returns <c>this</c>.
    /// </remarks>
    public ComponentView<TComponent> Template<TContext>(
        System.Func<TComponent, Microsoft.AspNetCore.Components.RenderFragment<TContext>?> selector,
        View content) => this;

    /// <summary>
    /// Design-time syntax binding a <c>RenderFragment&lt;TContext&gt;</c>-typed parameter to BlazorCodeFirst
    /// content that receives its context.
    /// </summary>
    /// <typeparam name="TContext">The template context type, inferred from the selected parameter.</typeparam>
    /// <param name="selector">Must name a settable <c>[Parameter]</c> of <c>RenderFragment&lt;TContext&gt;</c>.</param>
    /// <param name="content">An inline expression lambda from the template context to BlazorCodeFirst content.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    /// <remarks>
    /// <paramref name="content"/> must be an inline expression lambda or BCF3022 is reported. A real
    /// <c>RenderFragment&lt;TContext&gt;</c> value remains a scalar <see cref="Param{TValue}"/> value.
    /// This method is inert and returns <c>this</c>.
    /// </remarks>
    public ComponentView<TComponent> Template<TContext>(
        System.Func<TComponent, Microsoft.AspNetCore.Components.RenderFragment<TContext>?> selector,
        System.Func<TContext, View> content) => this;

    /// <summary>
    /// Design-time syntax two-way binding the parameter selected by <paramref name="selector"/>, which
    /// is Razor's <c>@bind-Value</c>. Unlike the element decoration, the names here are derived rather
    /// than written: the generator appends <c>{name}Changed</c>, and <c>{name}Expression</c> when
    /// <typeparamref name="TComponent"/> declares it. Both derivations are checked against the type,
    /// so a missing or mistyped <c>{name}Changed</c> is BCF3020 rather than a silent miss.
    /// </summary>
    /// <remarks>
    /// <paramref name="get"/> must be an inline lambda whose body is an assignable expression
    /// (BCF3017, BCF3018). Beyond building the setter, that lambda is what the generator passes as
    /// <c>{name}Expression</c>, which is how a component inside an <c>EditForm</c> identifies the
    /// bound field. No other spelling of the target can supply it.
    /// </remarks>
    /// <typeparam name="TValue">The parameter's value type, inferred from the selected property.</typeparam>
    /// <param name="selector">Selects the target parameter property, e.g. <c>c =&gt; c.Value</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda over an assignable expression.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Bind<TValue>(
        System.Func<TComponent, TValue> selector,
        System.Func<TValue> get) => this;

    /// <summary>Design-time syntax two-way binding with an explicit setter; see the getter-only overload.</summary>
    /// <typeparam name="TValue">The parameter's value type, inferred from the selected property.</typeparam>
    /// <param name="selector">Selects the target parameter property, e.g. <c>c =&gt; c.Value</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Bind<TValue>(
        System.Func<TComponent, TValue> selector,
        System.Func<TValue> get,
        System.Action<TValue> set) => this;

    /// <summary>Design-time syntax two-way binding with an explicit async setter; see the getter-only overload.</summary>
    /// <typeparam name="TValue">The parameter's value type, inferred from the selected property.</typeparam>
    /// <param name="selector">Selects the target parameter property, e.g. <c>c =&gt; c.Value</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Bind<TValue>(
        System.Func<TComponent, TValue> selector,
        System.Func<TValue> get,
        System.Func<TValue, System.Threading.Tasks.Task> set) => this;

    /// <summary>
    /// Design-time syntax binding <paramref name="children"/> to the component's <c>ChildContent</c>
    /// parameter, mirroring how Razor binds nested content.
    /// </summary>
    /// <param name="children">Mixed string and <see cref="View"/> content, in source order.</param>
    /// <returns>The marker <see cref="View"/>; never evaluated at runtime.</returns>
    /// <remarks>
    /// <typeparamref name="TComponent"/> must declare a settable <c>[Parameter]</c> named
    /// <c>ChildContent</c> of a fragment type, either
    /// <see cref="Microsoft.AspNetCore.Components.RenderFragment"/> or
    /// <c>RenderFragment&lt;TContext&gt;</c>; otherwise BCF3013 is reported. A generic one receives the
    /// children with its context discarded, so content that reads the context is written with
    /// <c>Template</c> instead. Use
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

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
    /// Design-time syntax giving the component a key, which is Razor's <c>@key</c>: the value Blazor's
    /// diff uses to decide which frame of the previous render this component is, independently of the
    /// sequence number that says where in the template it was written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared here rather than in <see cref="Decorations"/> because a component's builder is this type
    /// and not <c>ElementView</c>. The name is deliberately the one the element decoration carries: Razor
    /// spells both <c>@key</c>, and splitting the name would end the mirror at exactly the point the two
    /// receivers behave identically. It lowers to the same <c>SetKey</c>, which writes into the open
    /// component frame rather than appending one, so it consumes no sequence number and the parameters
    /// after it keep the numbers they would have had (<c>ARCHITECTURE.md</c> §2.7(E)).
    /// </para>
    /// <para>
    /// A key written as the literal <see langword="null"/> declines the key rather than setting one, and
    /// writing this on the content root of a keyed <c>ForEach</c> is BCF3032. Both rules are the element
    /// decoration's, unchanged.
    /// </para>
    /// </remarks>
    /// <param name="value">The key; any expression. A literal <see langword="null"/> declines the key.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Key(object? value) => this;

    /// <summary>
    /// Design-time syntax setting the component's render mode at the call site, which is Razor's
    /// <c>@rendermode</c>. The other half of the feature is an ordinary C# attribute on
    /// <typeparamref name="TComponent"/>'s own declaration, which needs nothing from this surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lowers to <c>AddComponentRenderMode</c>, which takes no sequence number, so like
    /// <see cref="Key"/> it costs the component no frame width. It does append a frame, so it is emitted
    /// after every parameter the component carries, scalar and slot alike
    /// (<c>ARCHITECTURE.md</c> §2.7(E)).
    /// </para>
    /// <para>
    /// The two forms cannot be combined. A <typeparamref name="TComponent"/> whose declaration carries a
    /// <c>RenderModeAttribute</c> has a fixed mode, and supplying one here makes the framework throw when
    /// it instantiates the component; that is BCF3034. The call site form is for the component that
    /// declares no mode of its own, which is the case it exists for: the same component rendered
    /// interactively from one page and statically from another. Note that the declaration form is an
    /// attribute the author declares: <c>RenderModeAttribute</c> is abstract and the framework ships no
    /// concrete subclass, since Razor's <c>@rendermode</c> directive generates one per component.
    /// </para>
    /// <para>
    /// A <see langword="null"/> mode sets none, which is what the framework's own overload does with it.
    /// A conditional mode is therefore written as an ordinary conditional expression, and no diagnostic
    /// stands between the two branches.
    /// </para>
    /// </remarks>
    /// <param name="mode">The render mode, or <see langword="null"/> to set none.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> RenderMode(
        Microsoft.AspNetCore.Components.IComponentRenderMode? mode) => this;

    /// <summary>
    /// Design-time syntax capturing the component instance, which is Razor's <c>@ref</c> on a component:
    /// <paramref name="capture"/> receives the <typeparamref name="TComponent"/> whenever it changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework's own <c>AddComponentReferenceCapture</c> hands back an <see cref="object"/> and
    /// leaves the cast to generated code. This takes an <see cref="System.Action{T}"/> of the component's
    /// own type instead, because <c>ComponentView&lt;TComponent&gt;</c> already knows what that type is and
    /// there is no reason to make the author write a cast the generator can write. The assignment itself
    /// stays at the call site, <c>.Ref(c =&gt; _row = c)</c>, for the reason the element decoration gives.
    /// </para>
    /// <para>
    /// Appends a frame, so it costs one sequence number, and it is emitted after every parameter the
    /// component carries, scalar and slot alike (<c>ARCHITECTURE.md</c> §2.7(E)).
    /// </para>
    /// </remarks>
    /// <param name="capture">Receives the component instance whenever it changes.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Ref(System.Action<TComponent> capture) => this;

    /// <summary>
    /// Design-time syntax adding a CSS class to this component call, which Blazor routes into the
    /// target's <c>[Parameter(CaptureUnmatchedValues = true)]</c> dictionary when it declares no
    /// <c>Class</c> parameter of its own (#314). Sugar for <c>.Attr("class", value)</c> — unlike the
    /// element decoration <c>Decorations.Class(ElementView, string?)</c>, this does not fold: writing
    /// it twice, or beside <c>.Attr("class", …)</c>, is BCF3010. A name that collides with a declared
    /// <c>[Parameter]</c>, case-insensitively, is BCF3042 instead — use <see cref="Param{TValue}"/> for
    /// that.
    /// </summary>
    /// <remarks>
    /// Declared here rather than in <see cref="Decorations"/> for the same reason as <see cref="Key"/>:
    /// a component's builder is this type and not <c>ElementView</c>. Unlike <c>Key</c>, this is not
    /// merely a style choice — a same-named <c>Decorations</c> extension on a second, unrelated
    /// extended type measurably degrades Roslyn's error recovery for every <em>other</em> failed
    /// <c>.Class</c>/<c>.Attr</c> call in a body, on any receiver, turning a recoverable single-candidate
    /// type into an unrecoverable error type. Measured: with that extension in place, an otherwise
    /// unrelated <c>.Param</c> call several frames away stopped resolving to a concrete method
    /// entirely. A member never enters extension-method candidate collection, so it carries none of
    /// that risk.
    /// </remarks>
    /// <param name="value">The CSS class value; any string expression.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Class(string? value) => this;

    /// <summary>
    /// Design-time syntax adding an arbitrary attribute to this component call, which Blazor routes
    /// into the target's <c>[Parameter(CaptureUnmatchedValues = true)]</c> dictionary when the name
    /// matches no declared parameter (#314). <paramref name="name"/> must be a non-empty compile-time
    /// constant, the same rule the element decoration <c>Decorations.Attr(ElementView, string, string?)</c>
    /// carries. A name that does match a declared <c>[Parameter]</c>, case-insensitively, is BCF3042:
    /// use <c>.Param(c =&gt; c.Name, value)</c> instead, so the value is type-checked.
    /// </summary>
    /// <remarks>Declared here rather than in <see cref="Decorations"/>; see <see cref="Class"/>'s remarks.</remarks>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Attr(string name, string? value) => this;

    /// <summary>Design-time syntax adding a boolean attribute to this component call; see the string overload.</summary>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="value">The attribute's presence; any <see langword="bool"/> expression.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Attr(string name, bool value) => this;

    /// <summary>
    /// Design-time syntax adding a valueless attribute to this component call; see the string overload.
    /// Equivalent to the <see langword="bool"/> overload with <see langword="true"/>.
    /// </summary>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <returns>The same inert builder for chaining; never evaluated at runtime.</returns>
    public ComponentView<TComponent> Attr(string name) => this;

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

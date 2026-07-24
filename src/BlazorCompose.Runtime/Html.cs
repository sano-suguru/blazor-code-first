namespace BlazorCompose;

/// <summary>
/// Design-time factory syntax for composing a <see cref="ComposeComponentBase.Body"/> expression as
/// literal HTML. Every member is inert: the BlazorCompose source generator analyzes calls to these
/// members and emits the equivalent <c>RenderTreeBuilder</c> instructions into the component's generated
/// <c>RenderBody</c>. They are never meant to run — at runtime they perform no work and return only a
/// default value, so they must not be invoked directly. Use qualified (<c>Html.Div(...)</c>); a
/// <c>using static BlazorCompose.Html;</c> is optional and risks identifier collisions with domain types.
/// </summary>
public static class Html
{
    /// <summary>Design-time syntax for an HTML <c>div</c> element with mixed string/element children.</summary>
    public static View Div(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>span</c> element with mixed string/element children.</summary>
    public static View Span(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>button</c> element; attach a handler with <c>.OnClick</c>.</summary>
    public static View Button(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an arbitrary HTML element; <paramref name="tag"/> must be a compile-time constant.</summary>
    public static View Element(string tag, params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for conditional rendering with an optional else branch.</summary>
    public static View If(bool condition, System.Func<View> then, System.Func<View>? otherwise = null) => default;

    /// <summary>Design-time syntax for a keyed list: one <paramref name="content"/> template per item.</summary>
    public static View ForEach<T>(
        System.Collections.Generic.IEnumerable<T> source,
        System.Func<T, object?> key,
        System.Func<T, View> content) => default;

    /// <summary>Design-time syntax for embedding an existing Blazor component into the compose tree.</summary>
    public static ComponentView<TComponent> Component<TComponent>()
        where TComponent : Microsoft.AspNetCore.Components.IComponent => default;
}

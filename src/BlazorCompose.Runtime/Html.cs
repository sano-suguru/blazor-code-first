namespace BlazorCompose;

/// <summary>
/// Design-time factory syntax for composing a <see cref="ComposeComponentBase.Body"/> expression as
/// literal HTML. Every member is inert: the BlazorCompose source generator analyzes calls to these
/// members and emits the equivalent <c>RenderTreeBuilder</c> instructions into the component's generated
/// <c>RenderBody</c>. They are never meant to run — at runtime they perform no work and return only a
/// default value, so they must not be invoked directly. Add <c>using static BlazorCompose.Html;</c> and
/// call factories unqualified (<c>Div(...)</c>); this is the recommended terse authoring form. The
/// qualified form (<c>Html.Div(...)</c>) remains available as an escape hatch for the rare file where an
/// imported factory name collides with a local identifier (for example, a domain type named <c>Component</c>).
/// </summary>
public static class Html
{
    /// <summary>Design-time syntax for an HTML <c>div</c> element with mixed string/element children.</summary>
    public static View Div(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>span</c> element with mixed string/element children.</summary>
    public static View Span(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>button</c> element; attach a handler with <c>.OnClick</c>.</summary>
    public static View Button(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>nav</c> element.</summary>
    public static View Nav(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>header</c> element.</summary>
    public static View Header(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>main</c> element.</summary>
    public static View Main(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>aside</c> element.</summary>
    public static View Aside(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>footer</c> element.</summary>
    public static View Footer(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>section</c> element.</summary>
    public static View Section(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>article</c> element.</summary>
    public static View Article(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>p</c> element.</summary>
    public static View P(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>h1</c> element.</summary>
    public static View H1(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>h2</c> element.</summary>
    public static View H2(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>h3</c> element.</summary>
    public static View H3(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>h4</c> element.</summary>
    public static View H4(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>h5</c> element.</summary>
    public static View H5(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>h6</c> element.</summary>
    public static View H6(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>ul</c> element.</summary>
    public static View Ul(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>ol</c> element.</summary>
    public static View Ol(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>li</c> element.</summary>
    public static View Li(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>a</c> element; set the target with <c>.Href</c>.</summary>
    public static View A(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for an HTML <c>img</c> element; set <c>.Src</c>/<c>.Alt</c>. This is a
    /// void element — passing children produces invalid HTML and is not prevented by the type system.</summary>
    public static View Img(params System.ReadOnlySpan<View> children) => default;

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

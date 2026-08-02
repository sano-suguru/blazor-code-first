namespace BlazorCodeFirst;

/// <summary>
/// Design-time syntax for composing a Compose design-time expression
/// (<see cref="ComposeComponentBase.Body"/> or <see cref="ComposeLayoutBase.Chrome"/>) as literal HTML.
/// Every member is inert: the BlazorCodeFirst source generator analyzes calls to these
/// members and emits the equivalent <c>RenderTreeBuilder</c> instructions into the component's generated
/// <c>RenderView</c>. They are never meant to run — at runtime they perform no work and return only a
/// default value, so they must not be invoked directly. Add <c>using static BlazorCodeFirst.Html;</c> and
/// write the element helpers, <c>If</c>/<c>ForEach</c>, <c>Component&lt;T&gt;()</c>, <c>Fragment</c>, and
/// <c>Raw</c> unqualified with children in brackets (<c>Div["text"]</c>); this is the recommended terse
/// authoring form. The qualified form (<c>Html.Div["text"]</c>) remains available as an escape hatch for
/// the rare file where an imported name collides with a local identifier (for example, a domain type
/// named <c>Component</c>).
/// </summary>
public static class Html
{
    /// <summary>Design-time syntax for an HTML <c>div</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Div => default;

    /// <summary>Design-time syntax for an HTML <c>span</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Span => default;

    /// <summary>Design-time syntax for an HTML <c>button</c> element; attach a handler with <c>.OnClick</c>
    /// and supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Button => default;

    /// <summary>Design-time syntax for an HTML <c>nav</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Nav => default;

    /// <summary>Design-time syntax for an HTML <c>header</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Header => default;

    /// <summary>Design-time syntax for an HTML <c>main</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Main => default;

    /// <summary>Design-time syntax for an HTML <c>aside</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Aside => default;

    /// <summary>Design-time syntax for an HTML <c>footer</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Footer => default;

    /// <summary>Design-time syntax for an HTML <c>section</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Section => default;

    /// <summary>Design-time syntax for an HTML <c>article</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Article => default;

    /// <summary>Design-time syntax for an HTML <c>p</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder P => default;

    /// <summary>Design-time syntax for an HTML <c>h1</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder H1 => default;

    /// <summary>Design-time syntax for an HTML <c>h2</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder H2 => default;

    /// <summary>Design-time syntax for an HTML <c>h3</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder H3 => default;

    /// <summary>Design-time syntax for an HTML <c>h4</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder H4 => default;

    /// <summary>Design-time syntax for an HTML <c>h5</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder H5 => default;

    /// <summary>Design-time syntax for an HTML <c>h6</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder H6 => default;

    /// <summary>Design-time syntax for an HTML <c>ul</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Ul => default;

    /// <summary>Design-time syntax for an HTML <c>ol</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Ol => default;

    /// <summary>Design-time syntax for an HTML <c>li</c> element; supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Li => default;

    /// <summary>Design-time syntax for an HTML <c>a</c> element; set the target with <c>.Href</c> and
    /// supply children with <c>[…]</c>.</summary>
    public static ElementBuilder A => default;

    /// <summary>Design-time syntax for an HTML <c>img</c> element; set <c>.Src</c>/<c>.Alt</c>. This is a
    /// void element — supplying children with <c>[…]</c> produces invalid HTML and is not prevented by the
    /// type system.</summary>
    public static ElementBuilder Img => default;

    /// <summary>Design-time syntax for an arbitrary HTML element; <paramref name="tag"/> must be a
    /// compile-time constant. Supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Element(string tag) => default;

    /// <summary>Design-time syntax for wrapper-less grouping: emits the children in sequence with no
    /// enclosing element (the React &lt;&gt;…&lt;/&gt; equivalent). A fragment opens no element, so it is
    /// non-keyable — it cannot be a ForEach content root (BCF3003) — and cannot be decorated: decorations
    /// apply to <see cref="ElementBuilder"/>, and a fragment is a <see cref="View"/>. Children may be zero
    /// or more mixed string/element values.</summary>
    public static View Fragment(params System.ReadOnlySpan<View> children) => default;

    /// <summary>Design-time syntax for injecting a trusted HTML string verbatim via AddMarkupContent
    /// (the MarkupString equivalent). TRUSTED CONTENT ONLY: the string is written to the DOM without
    /// escaping, so flowing untrusted data (user input, external responses) through here is an XSS vector.
    /// The value may be a string literal or a field/const reference (delivery-mechanism independent). Raw
    /// opens no element, so it cannot be a ForEach content root (BCF3003) and cannot be decorated:
    /// decorations apply to <see cref="ElementBuilder"/>, and Raw is a <see cref="View"/>.</summary>
    public static View Raw(string rawHtml) => default;

    /// <summary>Design-time syntax for conditional rendering with an optional else branch.</summary>
    public static View If(bool condition, System.Func<View> then, System.Func<View>? otherwise = null) => default;

    /// <summary>Design-time syntax for a keyed list: one <paramref name="content"/> template per item.</summary>
    public static View ForEach<T>(
        System.Collections.Generic.IEnumerable<T> source,
        System.Func<T, object?> key,
        System.Func<T, View> content) => default;

    /// <summary>Design-time syntax for embedding an existing Blazor component into the compose tree.</summary>
    /// <remarks>
    /// <typeparamref name="TComponent"/> must resolve while the source generator runs, because it is
    /// lowered to a literal <c>OpenComponent&lt;TComponent&gt;</c> call. A <c>.razor</c> component
    /// declared in the <em>same project</em> does not: the Razor compiler is itself a source generator,
    /// and source generators cannot observe each other's output, so such a type is reported as BCF3012.
    /// The same component in a referenced project or NuGet package resolves normally, as does a
    /// hand-authored C# component. Supply children through <see cref="ComponentView{TComponent}"/>'s
    /// indexer, which binds them to the component's <c>ChildContent</c> parameter.
    /// </remarks>
    public static ComponentView<TComponent> Component<TComponent>()
        where TComponent : Microsoft.AspNetCore.Components.IComponent => default;
}

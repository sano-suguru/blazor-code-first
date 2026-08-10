namespace BlazorCodeFirst;

/// <summary>
/// Design-time syntax for composing a BlazorCodeFirst design-time expression
/// (<see cref="BodyComponentBase.Body"/> or <see cref="ChromeLayoutBase.Chrome"/>) as literal HTML.
/// Every member is inert: the BlazorCodeFirst source generator analyzes calls to these
/// members and emits the equivalent <c>RenderTreeBuilder</c> instructions into the component's generated
/// <c>RenderView</c>. They are never meant to run: at runtime they perform no work and return only a
/// default value, so they must not be invoked directly. Add <c>using static BlazorCodeFirst.Html;</c> and
/// write the element helpers, <c>If</c>/<c>ForEach</c>, <c>Component&lt;T&gt;()</c>, <c>Fragment</c>, and
/// <c>Raw</c> unqualified with children in brackets (<c>Div["text"]</c>); this is the recommended terse
/// authoring form. The qualified form (<c>Html.Div["text"]</c>) remains available as an escape hatch for
/// the rare file where an imported name collides with a local identifier (for example, a domain type
/// named <c>Component</c>). Element helpers cover the conforming HTML element vocabulary; HTML validity
/// is not checked, so a void element with children or a <c>td</c> outside a <c>tr</c> compiles and
/// renders as written.
/// </summary>
public static partial class Html
{
    /// <summary>Design-time syntax for an arbitrary HTML element; <paramref name="tag"/> must be a
    /// compile-time constant. Supply children with <c>[…]</c>.</summary>
    public static ElementBuilder Element(string tag) => default;

    /// <summary>Design-time syntax for wrapper-less grouping: emits the children in sequence with no
    /// enclosing element (the React &lt;&gt;…&lt;/&gt; equivalent). A fragment opens no element, so it is
    /// non-keyable, it cannot be a ForEach content root (BCF3003), and cannot be decorated: decorations
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

    /// <summary>
    /// Design-time syntax marking where a <c>[Composable]</c> part places the content its caller supplied
    /// in brackets. Legal only in the body of a <c>[Composable]</c> method returning
    /// <see cref="ContentView"/>, and exactly once there; anywhere else it is BCF3025.
    /// </summary>
    /// <remarks>
    /// Named for HTML's own <c>&lt;slot&gt;</c>, which means the same thing. That element has no helper of
    /// its own — it is exclusion group 3 in <c>DESIGN.md</c> §4.1, because Blazor creates no shadow root and
    /// nothing would fill it — so the name is free, and taking it collides with no element.
    /// <para>
    /// Inert: the generator splices the caller's own child expressions into this position at compile time.
    /// Nothing is captured and nothing is invoked, so content that is written twice in a body is emitted
    /// twice rather than shared; a part therefore names its slot once. At runtime this always yields the
    /// default View.
    /// </para>
    /// </remarks>
    public static View Slot => default;

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

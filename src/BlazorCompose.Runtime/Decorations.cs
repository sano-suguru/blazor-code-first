namespace BlazorCompose;

/// <summary>
/// Design-time decoration syntax applied to a <see cref="View"/> in a Compose design-time expression
/// (<see cref="ComposeComponentBase.Body"/> or <see cref="ComposeLayoutBase.Chrome"/>).
/// </summary>
/// <remarks>
/// Like the <see cref="Html"/> factories, every member here is inert design-time syntax: the
/// BlazorCompose source generator reads the decoration chain statically and folds it into the owning
/// element's attributes. The members are never meant to run — at runtime they perform no work and
/// return the receiver unchanged, so they must not be invoked directly. Decorations live in a
/// dedicated static class (rather than on <see cref="View"/> itself) to keep <see cref="View"/> an
/// empty marker type.
/// </remarks>
public static class Decorations
{
    /// <summary>Design-time syntax adding a CSS class to the owning element's <c>class</c> attribute.</summary>
    /// <param name="view">The decorated view (an element factory such as Div/Span/Button).</param>
    /// <param name="class">The CSS class value; any string expression. Chain calls to add more.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static View Class(this View view, string @class) => view;

    /// <summary>Design-time syntax adding an <c>onclick</c> handler to the owning element.</summary>
    /// <param name="view">The decorated view (an element factory such as Div/Span/Button).</param>
    /// <param name="handler">The handler invoked on click; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static View OnClick(this View view, System.Action handler) => view;

    /// <summary>Design-time syntax setting the <c>href</c> attribute.</summary>
    public static View Href(this View view, string value) => view;

    /// <summary>Design-time syntax setting the <c>src</c> attribute.</summary>
    public static View Src(this View view, string value) => view;

    /// <summary>Design-time syntax setting the <c>alt</c> attribute.</summary>
    public static View Alt(this View view, string value) => view;

    /// <summary>Design-time syntax setting the <c>id</c> attribute.</summary>
    public static View Id(this View view, string value) => view;

    /// <summary>Design-time syntax setting the <c>type</c> attribute.</summary>
    public static View Type(this View view, string value) => view;

    /// <summary>Design-time syntax setting the <c>title</c> attribute.</summary>
    public static View Title(this View view, string value) => view;

    /// <summary>Design-time syntax setting the <c>role</c> attribute.</summary>
    public static View Role(this View view, string value) => view;

    /// <summary>
    /// Design-time syntax setting an arbitrary attribute. <paramref name="name"/> must be a non-empty
    /// compile-time constant. A name of <c>"class"</c> folds into the element's class channel; every other
    /// name is single-binding (a duplicate is reported). Styles are set here (<c>.Attr("style", …)</c>);
    /// there is deliberately no <c>.Style</c> shortcut, nudging toward external CSS + <c>.Class</c>.
    /// A bulk <c>.Attrs(IDictionary&lt;string, string&gt;)</c> splat is deferred (RM3+) and not yet available;
    /// bind each attribute individually with this overload or a named shortcut until then.
    /// </summary>
    public static View Attr(this View view, string name, string value) => view;

    /// <summary>
    /// Design-time syntax adding an event handler. <paramref name="eventName"/> is the full HTML event
    /// attribute name including the <c>on</c> prefix (for example <c>"onclick"</c>, <c>"onmouseenter"</c>);
    /// it is never prefixed automatically. Must be a non-empty compile-time constant.
    /// </summary>
    public static View On(this View view, string eventName, System.Action handler) => view;

    /// <summary>Design-time syntax adding an async event handler; see the synchronous overload.</summary>
    public static View On(this View view, string eventName, System.Func<System.Threading.Tasks.Task> handler) => view;

    /// <summary>Design-time syntax adding an async <c>onclick</c> handler.</summary>
    public static View OnClick(this View view, System.Func<System.Threading.Tasks.Task> handler) => view;
}

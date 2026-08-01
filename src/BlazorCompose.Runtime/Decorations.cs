namespace BlazorCompose;

/// <summary>
/// Design-time decoration syntax applied to an <see cref="ElementBuilder"/> in a Compose design-time
/// expression (<see cref="ComposeComponentBase.Body"/> or <see cref="ComposeLayoutBase.Chrome"/>).
/// </summary>
/// <remarks>
/// Like the <see cref="Html"/> factories, every member here is inert design-time syntax: the
/// BlazorCompose source generator reads the decoration chain statically and folds it into the owning
/// element's attributes. The members are never meant to run — at runtime they perform no work and
/// return the receiver unchanged, so they must not be invoked directly. Decorations live in a
/// dedicated static class (rather than on <see cref="ElementBuilder"/> itself) because they are
/// extension methods on the builder: an element's attributes are written before its children
/// (<c>Div.Class("card")["text"]</c>), so the builder's own indexer is reserved for the children
/// channel and decorations attach from the outside.
/// </remarks>
public static class Decorations
{
    /// <summary>Design-time syntax adding a CSS class to the owning element's <c>class</c> attribute.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="class">The CSS class value; any string expression. Chain calls to add more.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Class(this ElementBuilder element, string @class) => element;

    /// <summary>Design-time syntax adding an <c>onclick</c> handler to the owning element.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="handler">The handler invoked on click; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder OnClick(this ElementBuilder element, System.Action handler) => element;

    /// <summary>Design-time syntax setting the <c>href</c> attribute.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Href(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>src</c> attribute.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Src(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>alt</c> attribute.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Alt(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>id</c> attribute.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Id(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>type</c> attribute.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Type(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>title</c> attribute.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Title(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>role</c> attribute.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Role(this ElementBuilder element, string value) => element;

    /// <summary>
    /// Design-time syntax setting an arbitrary attribute. <paramref name="name"/> must be a non-empty
    /// compile-time constant. A name of <c>"class"</c> folds into the element's class channel; every other
    /// name is single-binding (a duplicate is reported). Styles are set here (<c>.Attr("style", …)</c>);
    /// there is deliberately no <c>.Style</c> shortcut, nudging toward external CSS + <c>.Class</c>.
    /// A bulk <c>.Attrs(IDictionary&lt;string, string&gt;)</c> splat is deferred (RM3+) and not yet available;
    /// bind each attribute individually with this overload or a named shortcut until then.
    /// </summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Attr(this ElementBuilder element, string name, string value) => element;

    /// <summary>
    /// Design-time syntax adding an event handler. <paramref name="eventName"/> is the full HTML event
    /// attribute name including the <c>on</c> prefix (for example <c>"onclick"</c>, <c>"onmouseenter"</c>);
    /// it is never prefixed automatically. Must be a non-empty compile-time constant.
    /// </summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="eventName">The full HTML event attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="handler">The handler invoked on the event; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder On(this ElementBuilder element, string eventName, System.Action handler) => element;

    /// <summary>Design-time syntax adding an async event handler; see the synchronous overload.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="eventName">The full HTML event attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="handler">The async handler invoked on the event; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder On(
        this ElementBuilder element, string eventName, System.Func<System.Threading.Tasks.Task> handler) => element;

    /// <summary>Design-time syntax adding an async <c>onclick</c> handler.</summary>
    /// <param name="element">The decorated element (a factory such as Div/Span/Button).</param>
    /// <param name="handler">The async handler invoked on click; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder OnClick(
        this ElementBuilder element, System.Func<System.Threading.Tasks.Task> handler) => element;
}

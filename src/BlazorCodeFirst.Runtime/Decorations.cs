namespace BlazorCodeFirst;

/// <summary>
/// Design-time decoration syntax applied to an <see cref="ElementBuilder"/> in a BlazorCodeFirst design-time
/// expression (<see cref="BodyComponentBase.Body"/> or <see cref="ChromeLayoutBase.Chrome"/>).
/// </summary>
/// <remarks>
/// Like the <see cref="Html"/> element helpers, every member here is inert design-time syntax: the
/// BlazorCodeFirst source generator reads the decoration chain statically and folds it into the owning
/// element's attributes. The members are never meant to run: at runtime they perform no work and
/// return the receiver unchanged, so they must not be invoked directly. Decorations live in a
/// dedicated static class (rather than on <see cref="ElementBuilder"/> itself) because they are
/// extension methods on the builder: an element's attributes are written before its children
/// (<c>Div.Class("card")["text"]</c>), so the builder's own indexer is reserved for the children
/// channel and decorations attach from the outside.
/// </remarks>
public static class Decorations
{
    /// <summary>Design-time syntax adding a CSS class to the owning element's <c>class</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="class">The CSS class value; any string expression. Chain calls to add more.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Class(this ElementBuilder element, string @class) => element;

    /// <summary>Design-time syntax adding an <c>onclick</c> handler to the owning element.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="handler">The handler invoked on click; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder OnClick(this ElementBuilder element, System.Action handler) => element;

    /// <summary>Design-time syntax setting the <c>href</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Href(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>src</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Src(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>alt</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Alt(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>id</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Id(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>type</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Type(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>title</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Title(this ElementBuilder element, string value) => element;

    /// <summary>Design-time syntax setting the <c>role</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
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
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Attr(this ElementBuilder element, string name, string value) => element;

    /// <summary>
    /// Design-time syntax setting an attribute from a <see langword="bool"/>, which is Blazor's
    /// conditional-attribute form: <see langword="true"/> renders the attribute with an empty value
    /// (<c>disabled</c>, <c>checked</c>, <c>hidden</c> and the rest of HTML's boolean attributes read
    /// that as set), and <see langword="false"/> omits it entirely. <paramref name="name"/> follows the
    /// same rule as the string overload: a non-empty compile-time constant.
    /// </summary>
    /// <remarks>
    /// This is the only non-<see langword="string"/> overload, and deliberately so (#158). A value of any
    /// other type is formatted under whatever culture the formatting thread carries at render time —
    /// measured, not under the culture in effect while the component builds its frames — so an
    /// <c>object</c> overload's output would depend on ambient state the call site cannot see, and the
    /// generator could never fold it. Write such a value out at the call site instead, where the culture
    /// is a visible choice: <c>.Attr("tabindex", index.ToString(CultureInfo.InvariantCulture))</c>. A
    /// <see langword="bool"/> has nothing to format and neither problem.
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="value">The attribute's presence; any <see langword="bool"/> expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Attr(this ElementBuilder element, string name, bool value) => element;

    /// <summary>
    /// Design-time syntax adding an event handler. <paramref name="eventName"/> is the full HTML event
    /// attribute name including the <c>on</c> prefix (for example <c>"onclick"</c>, <c>"onmouseenter"</c>);
    /// it is never prefixed automatically, and a name that does not begin with <c>on</c> is BCF3019.
    /// Blazor would add such a name as a plain attribute whose handler never fires. Must be a non-empty
    /// compile-time constant.
    /// </summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="eventName">The full HTML event attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="handler">The handler invoked on the event; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder On(this ElementBuilder element, string eventName, System.Action handler) => element;

    /// <summary>Design-time syntax adding an async event handler; see the synchronous overload.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="eventName">The full HTML event attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="handler">The async handler invoked on the event; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder On(
        this ElementBuilder element, string eventName, System.Func<System.Threading.Tasks.Task> handler) => element;

    /// <summary>
    /// Design-time syntax adding an event handler that receives the event's arguments.
    /// <paramref name="eventName"/> follows the same rule as the argument-less overload: the full HTML
    /// event attribute name including the <c>on</c> prefix, a non-empty compile-time constant.
    /// </summary>
    /// <remarks>
    /// <typeparamref name="TArgs"/> is inferred from an explicitly typed lambda parameter
    /// (<c>.On("oninput", (ChangeEventArgs e) =&gt; …)</c>), so writing it out is legal but never necessary.
    /// Razor infers the argument type from the event name through its <c>[EventHandler]</c> metadata; a
    /// string-named decoration cannot, because C# overload resolution runs before the generator observes
    /// the expression and the generator does not influence binding. Nothing here checks that
    /// <typeparamref name="TArgs"/> is the type the named event actually delivers.
    /// </remarks>
    /// <typeparam name="TArgs">The event argument type the handler receives.</typeparam>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="eventName">The full HTML event attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="handler">The handler invoked on the event; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder On<TArgs>(
        this ElementBuilder element, string eventName, System.Action<TArgs> handler)
        where TArgs : System.EventArgs => element;

    /// <summary>
    /// Design-time syntax adding an async event handler that receives the event's arguments; see the
    /// synchronous overload.
    /// </summary>
    /// <typeparam name="TArgs">The event argument type the handler receives.</typeparam>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="eventName">The full HTML event attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="handler">The async handler invoked on the event; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder On<TArgs>(
        this ElementBuilder element, string eventName,
        System.Func<TArgs, System.Threading.Tasks.Task> handler)
        where TArgs : System.EventArgs => element;

    /// <summary>Design-time syntax adding an async <c>onclick</c> handler.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="handler">The async handler invoked on click; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder OnClick(
        this ElementBuilder element, System.Func<System.Threading.Tasks.Task> handler) => element;

    /// <summary>
    /// Design-time syntax binding an attribute and an event to a single target, which is Razor's
    /// <c>@bind</c>. <paramref name="attributeName"/> receives the current value and
    /// <paramref name="eventName"/> writes it back. Both are non-empty compile-time constants, and
    /// neither is inferred: <paramref name="eventName"/> is the full HTML event attribute name
    /// including the <c>on</c> prefix, exactly as <see cref="On(ElementBuilder, string, System.Action)"/>
    /// requires it.
    /// </summary>
    /// <remarks>
    /// Razor reads the literal <c>type="checkbox"</c> out of markup to decide between <c>value</c> and
    /// <c>checked</c>. This surface cannot: its <c>type</c> is an expression
    /// (<c>Input.Type(kind)</c>), so there is nothing to check an inference against, and a silent
    /// fallback would leave a checkbox bound to the wrong attribute with no diagnostic. The author
    /// writes both names instead.
    /// <para>
    /// <paramref name="get"/> must be an inline lambda whose body is an assignable expression
    /// (BCF3017, BCF3018): the generator places that body on the left of an assignment to build the
    /// setter. Use the overload taking an explicit setter for a computed target or to normalize the
    /// incoming value.
    /// </para>
    /// <para>
    /// Only <see langword="string"/> and <see langword="bool"/> are bindable, for the reason recorded
    /// on the <see langword="bool"/> <see cref="Attr(ElementBuilder, string, bool)"/> overload (#158):
    /// any other type is formatted at render time under the formatting thread's culture. Razor answers
    /// that by injecting a culture chosen from the element's literal <c>type</c>, which this surface
    /// does not read. Bind such a value through the explicit-setter overload, where the culture is a
    /// visible choice at the call site.
    /// </para>
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Input</c>, <c>Textarea</c>, …).</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda over an assignable expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Bind(
        this ElementBuilder element, string attributeName, string eventName,
        System.Func<string> get) => element;

    /// <summary>Design-time syntax binding with an explicit setter; see the getter-only overload.</summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Bind(
        this ElementBuilder element, string attributeName, string eventName,
        System.Func<string> get, System.Action<string> set) => element;

    /// <summary>Design-time syntax binding with an explicit async setter; see the getter-only overload.</summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Bind(
        this ElementBuilder element, string attributeName, string eventName,
        System.Func<string> get, System.Func<string, System.Threading.Tasks.Task> set) => element;

    /// <summary>
    /// Design-time syntax binding a <see langword="bool"/> attribute, which is HTML's boolean-attribute
    /// form (<c>checked</c>, and the conditional-omission behaviour recorded on
    /// <see cref="Attr(ElementBuilder, string, bool)"/>); see the <see langword="string"/> overload for
    /// the rest.
    /// </summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda over an assignable expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Bind(
        this ElementBuilder element, string attributeName, string eventName,
        System.Func<bool> get) => element;

    /// <summary>Design-time syntax binding a <see langword="bool"/> with an explicit setter.</summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Bind(
        this ElementBuilder element, string attributeName, string eventName,
        System.Func<bool> get, System.Action<bool> set) => element;

    /// <summary>Design-time syntax binding a <see langword="bool"/> with an explicit async setter.</summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementBuilder Bind(
        this ElementBuilder element, string attributeName, string eventName,
        System.Func<bool> get, System.Func<bool, System.Threading.Tasks.Task> set) => element;
}

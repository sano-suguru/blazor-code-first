namespace BlazorCodeFirst;

/// <summary>
/// Design-time decoration syntax applied to an <see cref="ElementView"/> in a BlazorCodeFirst design-time
/// expression (<see cref="BodyComponentBase.Body"/> or <see cref="ChromeLayoutBase.Chrome"/>).
/// </summary>
/// <remarks>
/// Like the <see cref="Html"/> element helpers, every member here is inert design-time syntax: the
/// BlazorCodeFirst source generator reads the decoration chain statically and folds it into the owning
/// element's attributes. The members are never meant to run: at runtime they perform no work and
/// return the receiver unchanged, so they must not be invoked directly. Decorations live in a
/// dedicated static class (rather than on <see cref="ElementView"/> itself) because they are
/// extension methods on the builder: an element's attributes are written before its children
/// (<c>Div.Class("card")["text"]</c>), so the builder's own indexer is reserved for the children
/// channel and decorations attach from the outside.
/// </remarks>
public static class Decorations
{
    /// <summary>Design-time syntax adding a CSS class to the owning element's <c>class</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The CSS class value; any string expression. Chain calls to add more.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Class(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax adding an <c>onclick</c> handler to the owning element.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="handler">The handler invoked on click; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView OnClick(this ElementView element, System.Action handler) => element;

    /// <summary>Design-time syntax setting the <c>href</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Href(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>src</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Src(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>alt</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Alt(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>id</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Id(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>type</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Type(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>title</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Title(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>role</c> attribute.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Role(this ElementView element, string? value) => element;

    /// <summary>
    /// Design-time syntax setting an arbitrary attribute. <paramref name="name"/> must be a non-empty
    /// compile-time constant. A name of <c>"class"</c> folds into the element's class channel; every other
    /// name is single-binding (a duplicate is reported). <c>style</c> is one of those ordinary names:
    /// <c>.Attr("style", …)</c> writes it, and two of them on one element is BCF3010 rather than a
    /// second fold. There is deliberately no <c>.Style</c> shortcut (#321, <c>DESIGN.md</c> §4.1).
    /// There is no bulk <c>.Attrs(…)</c> splat and there will not be one (#308 / #320): a name that
    /// arrives at runtime cannot join the class channel's compile-time fold, and the duplicate check
    /// cannot see it. Bind each attribute individually with this overload or a named shortcut. The
    /// reasons are in <c>ARCHITECTURE.md</c> 付録B.14.
    /// </summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Attr(this ElementView element, string name, string? value) => element;

    /// <summary>
    /// Design-time syntax setting an attribute from a <see langword="bool"/>, which is Blazor's
    /// conditional-attribute form: <see langword="true"/> renders the attribute with an empty value
    /// (<c>disabled</c>, <c>checked</c>, <c>hidden</c> and the rest of HTML's boolean attributes read
    /// that as set), and <see langword="false"/> omits it entirely. <paramref name="name"/> follows the
    /// same rule as the string overload: a non-empty compile-time constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Write <see cref="Attr(ElementView, string)"/> instead where the attribute is always present:
    /// this overload is for the conditional case, and a literal <see langword="true"/> says nothing that
    /// the bare spelling does not.
    /// </para>
    /// This is the only non-<see langword="string"/> overload, and deliberately so (#158). A value of any
    /// other type is formatted under whatever culture the formatting thread carries at render time —
    /// measured, not under the culture in effect while the component builds its frames — so an
    /// <c>object</c> overload's output would depend on ambient state the call site cannot see, and the
    /// generator could never fold it. Write such a value out at the call site instead, where the culture
    /// is a visible choice: <c>.Attr("tabindex", index.ToString(CultureInfo.InvariantCulture))</c>. A
    /// <see langword="bool"/> has nothing to format and neither problem.
    /// <para>
    /// One name is closed to this overload: <c>"class"</c> folds into the class channel, which joins its
    /// decorations into one value as text, so that channel takes a <see cref="string"/> and nothing else.
    /// A <see langword="bool"/> there means one thing on an element carrying a single class decoration and
    /// another on an element carrying two (#159). That is BCF3023; write a conditional class as a string
    /// expression, <c>.Class(active ? "on" : null)</c>.
    /// </para>
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="value">The attribute's presence; any <see langword="bool"/> expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Attr(this ElementView element, string name, bool value) => element;

    /// <summary>
    /// Design-time syntax setting an attribute that is present with no value, which is how HTML writes one:
    /// <c>&lt;button disabled&gt;</c>, <c>&lt;video controls&gt;</c>. Equivalent in every respect to
    /// <see cref="Attr(ElementView, string, bool)"/> with <see langword="true"/>, which is the spelling
    /// for the conditional case. <paramref name="name"/> follows the same rule as the other overloads: a
    /// non-empty compile-time constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An overload of its own rather than a default on the <see langword="bool"/> one (#178). A default
    /// there is RS0027: an API carrying an optional parameter must have the most parameters among its
    /// overloads, and the <see langword="string"/> overload has just as many. The rule's own hazard —
    /// a shorter overload silently losing calls to the longer one — is what this shape avoids by being
    /// that shorter overload outright.
    /// </para>
    /// <para>
    /// One name is closed to it, for the reason on the <see langword="bool"/> overload: <c>.Attr("class")</c>
    /// carries a presence into a channel that joins its decorations as text, and a presence has no text.
    /// That is BCF3023, reported at the decoration's name because there is no value argument to point at.
    /// </para>
    /// <para>
    /// The cost is that a value left off by accident, <c>.Attr("aria-label")</c>, is now a valueless
    /// attribute rather than a compile error.
    /// </para>
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="name">The attribute name; must be a non-empty compile-time constant.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Attr(this ElementView element, string name) => element;

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
    public static ElementView On(this ElementView element, string eventName, System.Action handler) => element;

    /// <summary>Design-time syntax adding an async event handler; see the synchronous overload.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="eventName">The full HTML event attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="handler">The async handler invoked on the event; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView On(
        this ElementView element, string eventName, System.Func<System.Threading.Tasks.Task> handler) => element;

    /// <summary>
    /// Design-time syntax adding an event handler that receives the event's arguments.
    /// <paramref name="eventName"/> follows the same rule as the argument-less overload: the full HTML
    /// event attribute name including the <c>on</c> prefix, a non-empty compile-time constant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <typeparamref name="TArgs"/> is inferred from an explicitly typed lambda parameter
    /// (<c>.On("oninput", (ChangeEventArgs e) =&gt; …)</c>), so writing it out is legal but never necessary.
    /// Razor <em>infers</em> the argument type from the event name through its <c>[EventHandler]</c>
    /// metadata; a string-named decoration cannot, because C# overload resolution runs before the generator
    /// observes the expression and the generator does not influence binding.
    /// </para>
    /// <para>
    /// It is nevertheless checked against the same metadata: an argument type the named event cannot deliver
    /// is BCF3028, and so is a <typeparamref name="TArgs"/> that is not a <see cref="System.EventArgs"/> at
    /// all. The test is assignability, so a base type is accepted
    /// (<c>.On("onclick", (System.EventArgs e) =&gt; …)</c> receives a <c>MouseEventArgs</c>), and an event
    /// with no <c>[EventHandler]</c> registration has no mapping and is not checked.
    /// </para>
    /// </remarks>
    /// <typeparam name="TArgs">The event argument type the handler receives.</typeparam>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="eventName">The full HTML event attribute name; must be a non-empty compile-time constant.</param>
    /// <param name="handler">The handler invoked on the event; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView On<TArgs>(
        this ElementView element, string eventName, System.Action<TArgs> handler)
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
    public static ElementView On<TArgs>(
        this ElementView element, string eventName,
        System.Func<TArgs, System.Threading.Tasks.Task> handler)
        where TArgs : System.EventArgs => element;

    /// <summary>Design-time syntax adding an async <c>onclick</c> handler.</summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="handler">The async handler invoked on click; lowered to an EventCallback.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView OnClick(
        this ElementView element, System.Func<System.Threading.Tasks.Task> handler) => element;

    /// <summary>
    /// Design-time syntax binding an attribute and an event to a single target, which is Razor's
    /// <c>@bind</c>. <paramref name="attributeName"/> receives the current value and
    /// <paramref name="eventName"/> writes it back. Both are non-empty compile-time constants, and
    /// neither is inferred: <paramref name="eventName"/> is the full HTML event attribute name
    /// including the <c>on</c> prefix, exactly as <see cref="On(ElementView, string, System.Action)"/>
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
    /// on the <see langword="bool"/> <see cref="Attr(ElementView, string, bool)"/> overload (#158):
    /// any other type is formatted at render time under the formatting thread's culture. Razor answers
    /// that by injecting a culture chosen from the element's literal <c>type</c>, which this surface
    /// does not read. Bind such a value through the explicit-setter overload, where the culture is a
    /// visible choice at the call site.
    /// </para>
    /// <para>
    /// Measured against a real dispatch (BindRenderingTests, <c>EmptyInput_DeliversEmptyStringNotNull</c>):
    /// an empty text input's <c>oninput</c> delivers <c>""</c> to the setter, not <c>null</c>. That is
    /// why <paramref name="get"/> and the explicit-setter overload's setter both take a non-nullable
    /// <see langword="string"/>: the generated setter itself never has to guard against a null it will
    /// not receive. The framework's own binder helper still annotates its own parameter nullable
    /// defensively; the generated file's preamble accounts for that, not this surface.
    /// </para>
    /// <para>
    /// <paramref name="attributeName"/> may be <c>"class"</c>, but not on an element that also carries
    /// <see cref="Class(ElementView, string)"/> or <c>.Attr("class", …)</c>. Those two fold into one
    /// attribute and a binding does not join them, so the element would be emitted carrying <c>class</c>
    /// twice. That is BCF3024; supply the whole class value from <paramref name="get"/>, or drop the
    /// binding (#188).
    /// </para>
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Input</c>, <c>Textarea</c>, …).</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda over an assignable expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind(
        this ElementView element, string attributeName, string eventName,
        System.Func<string> get) => element;

    /// <summary>Design-time syntax binding with an explicit setter; see the getter-only overload.</summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind(
        this ElementView element, string attributeName, string eventName,
        System.Func<string> get, System.Action<string> set) => element;

    /// <summary>Design-time syntax binding with an explicit async setter; see the getter-only overload.</summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind(
        this ElementView element, string attributeName, string eventName,
        System.Func<string> get, System.Func<string, System.Threading.Tasks.Task> set) => element;

    /// <summary>
    /// Design-time syntax binding a <see langword="bool"/> attribute, which is HTML's boolean-attribute
    /// form (<c>checked</c>, and the conditional-omission behaviour recorded on
    /// <see cref="Attr(ElementView, string, bool)"/>); see the <see langword="string"/> overload for
    /// the rest.
    /// </summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda over an assignable expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind(
        this ElementView element, string attributeName, string eventName,
        System.Func<bool> get) => element;

    /// <summary>Design-time syntax binding a <see langword="bool"/> with an explicit setter.</summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind(
        this ElementView element, string attributeName, string eventName,
        System.Func<bool> get, System.Action<bool> set) => element;

    /// <summary>Design-time syntax binding a <see langword="bool"/> with an explicit async setter.</summary>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind(
        this ElementView element, string attributeName, string eventName,
        System.Func<bool> get, System.Func<bool, System.Threading.Tasks.Task> set) => element;

    /// <summary>
    /// Design-time syntax binding a value of any type through the framework's own converter, with the
    /// culture written at the call site; see the <see langword="string"/> overload for the rest.
    /// </summary>
    /// <remarks>
    /// <paramref name="culture"/> cannot be omitted. #158 withheld non-string values because the
    /// formatting culture was ambient state no caller could choose; an argument that must be written
    /// does not reach that reason (#307). Razor picks the culture from the element's literal
    /// <c>type</c>, which this surface does not read, so <c>type="number"</c> and <c>type="date"</c>
    /// need <see cref="System.Globalization.CultureInfo.InvariantCulture"/> written here. That mistake
    /// is not diagnosed: <c>.Type(kind)</c> may be an expression, and a check that only fired on a
    /// constant would catch the same error or not depending on how it was spelled.
    /// <para>
    /// No type is excluded. The generated attribute frame receives
    /// <c>BindConverter.FormatValue(value, culture:)</c>, which is an already-formatted string for every
    /// type, so the formatting no longer depends on the rendering thread. A type the framework's
    /// converter cannot handle throws <see cref="System.InvalidOperationException"/> naming the type,
    /// which is not the silent failure this surface's diagnostics answer.
    /// </para>
    /// </remarks>
    /// <typeparam name="TValue">The bound value's type.</typeparam>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda over an assignable expression.</param>
    /// <param name="culture">Formats the value on the way out and parses it on the way back.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind<TValue>(
        this ElementView element, string attributeName, string eventName,
        System.Func<TValue> get, System.Globalization.CultureInfo culture) => element;

    /// <summary>Design-time syntax binding a value of any type with an explicit setter; see the getter-only overload.</summary>
    /// <typeparam name="TValue">The bound value's type.</typeparam>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <param name="culture">Formats the value on the way out and parses it on the way back.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind<TValue>(
        this ElementView element, string attributeName, string eventName,
        System.Func<TValue> get, System.Action<TValue> set,
        System.Globalization.CultureInfo culture) => element;

    /// <summary>Design-time syntax binding a value of any type with an explicit async setter; see the getter-only overload.</summary>
    /// <typeparam name="TValue">The bound value's type.</typeparam>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <param name="culture">Formats the value on the way out and parses it on the way back.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind<TValue>(
        this ElementView element, string attributeName, string eventName,
        System.Func<TValue> get, System.Func<TValue, System.Threading.Tasks.Task> set,
        System.Globalization.CultureInfo culture) => element;

    /// <summary>
    /// Design-time syntax binding a value of any type with an explicit format string; see the
    /// getter-only overload taking only a culture.
    /// </summary>
    /// <remarks>
    /// The framework declares format-taking converters for <see cref="System.DateTime"/>,
    /// <see cref="System.DateTimeOffset"/>, <see cref="System.DateOnly"/>, <see cref="System.TimeOnly"/>
    /// and their nullable forms only. Any other <typeparamref name="TValue"/> is BCF3031. A format is
    /// how <c>&lt;input type="date"&gt;</c> is bound at all, since the browser requires
    /// <c>yyyy-MM-dd</c> and this surface cannot read the element's <c>type</c>.
    /// </remarks>
    /// <typeparam name="TValue">The bound value's type.</typeparam>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda over an assignable expression.</param>
    /// <param name="format">The format string handed to the framework's converter in both directions.</param>
    /// <param name="culture">Formats the value on the way out and parses it on the way back.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind<TValue>(
        this ElementView element, string attributeName, string eventName,
        System.Func<TValue> get, string format,
        System.Globalization.CultureInfo culture) => element;

    /// <summary>Design-time syntax binding a value of any type with a format and an explicit setter; see the format overload.</summary>
    /// <typeparam name="TValue">The bound value's type.</typeparam>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <param name="format">The format string handed to the framework's converter in both directions.</param>
    /// <param name="culture">Formats the value on the way out and parses it on the way back.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind<TValue>(
        this ElementView element, string attributeName, string eventName,
        System.Func<TValue> get, System.Action<TValue> set, string format,
        System.Globalization.CultureInfo culture) => element;

    /// <summary>Design-time syntax binding a value of any type with a format and an explicit async setter; see the format overload.</summary>
    /// <typeparam name="TValue">The bound value's type.</typeparam>
    /// <param name="element">The element being decorated.</param>
    /// <param name="attributeName">The attribute carrying the value; a non-empty compile-time constant.</param>
    /// <param name="eventName">The full HTML event attribute name; a non-empty compile-time constant beginning with <c>on</c>.</param>
    /// <param name="get">Reads the current value; an inline lambda.</param>
    /// <param name="set">Writes the new value back. May be a lambda or a method group.</param>
    /// <param name="format">The format string handed to the framework's converter in both directions.</param>
    /// <param name="culture">Formats the value on the way out and parses it on the way back.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Bind<TValue>(
        this ElementView element, string attributeName, string eventName,
        System.Func<TValue> get, System.Func<TValue, System.Threading.Tasks.Task> set, string format,
        System.Globalization.CultureInfo culture) => element;

    /// <summary>
    /// Design-time syntax giving the element a key, which is Razor's <c>@key</c>: the value Blazor's diff
    /// uses to decide which frame of the previous render this element is, independently of the sequence
    /// number that says where in the template it was written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not an attribute, and so outside the fold every other decoration here takes part in. It lowers to
    /// <c>SetKey</c>, which writes into the element frame that is already open rather than appending one,
    /// so it consumes no sequence number and the attributes after it keep the numbers they would have had
    /// (<c>ARCHITECTURE.md</c> §2.7(E)). It does stop the element folding into a markup frame: markup has
    /// no way to carry a key.
    /// </para>
    /// <para>
    /// A key written as the literal <see langword="null"/> declines the key rather than setting one, the
    /// same reading <c>ForEach(source, key: null, content)</c> gets (#172), and no <c>SetKey</c> is
    /// emitted. A non-constant expression that happens to evaluate to <see langword="null"/> is not that
    /// case: the call is emitted, and <c>SetKey</c> ignores a null value at runtime.
    /// </para>
    /// <para>
    /// Writing this on the content root of a keyed <c>ForEach</c> is BCF3032. The loop already applies its
    /// own key to that frame, and the two would be one <c>SetKey</c> overwriting the other.
    /// </para>
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">The key; any expression. A literal <see langword="null"/> declines the key.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Key(this ElementView element, object? value) => element;

    /// <summary>
    /// Design-time syntax capturing a reference to the rendered element, which is Razor's <c>@ref</c>:
    /// <paramref name="capture"/> receives the <see cref="Microsoft.AspNetCore.Components.ElementReference"/>
    /// whenever it changes, and that reference is what JS interop takes to reach the real DOM node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Razor names a field (<c>@ref="_input"</c>) and its generated code assigns to it. Here the
    /// assignment is written at the call site, <c>.Ref(r =&gt; _input = r)</c>, because
    /// <c>AddElementReferenceCapture</c> takes an <see cref="System.Action{T}"/> and there is nothing for
    /// the generator to build. Nothing has to be a settable member this compiler can name: the lambda is
    /// carried into generated code under the same rules as any other transplanted expression.
    /// </para>
    /// <para>
    /// Unlike <see cref="Key(ElementView, object?)"/> this appends a frame of its own, so it costs the
    /// element one sequence number, and it is emitted after every attribute, event and binding the element
    /// carries (<c>ARCHITECTURE.md</c> §2.7(E)). It stops the element folding into a markup frame for the
    /// same reason a key does.
    /// </para>
    /// <para>
    /// The captured reference is only usable once the element exists, which is
    /// <c>OnAfterRender</c> onward. Reading it while the design-time expression is being evaluated reaches
    /// a reference to nothing, and this surface does not diagnose that: the read happens in the author's
    /// own C#, not in anything the generator sees.
    /// </para>
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Input</c>, <c>Element("…")</c>, …).</param>
    /// <param name="capture">Receives the element reference whenever it changes.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Ref(
        this ElementView element,
        System.Action<Microsoft.AspNetCore.Components.ElementReference> capture) => element;

    /// <summary>
    /// Design-time syntax making the preceding event call <c>preventDefault()</c> in the browser, which is
    /// Razor's <c>@onwheel:preventDefault</c>. It attaches to the event written before it on the same
    /// element, so <c>.On("onwheel", Zoom).PreventDefault()</c> modifies <c>onwheel</c>. A modifier with no
    /// event before it is BCF3035, and a second one for the same event is BCF3036. The event has to come
    /// from <c>.On</c> or a named event shortcut: <c>.Bind</c> writes its event into a channel this cannot
    /// reach, so a modifier after one is BCF3037 rather than a modifier of the binding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two overloads rather than one carrying a default, for the reason recorded on
    /// <see cref="Attr(ElementView, string)"/>: an optional parameter here is RS0026, because the valueless
    /// spelling and the <see langword="bool"/> one are overloads of a single name.
    /// </para>
    /// <para>
    /// Unlike <see cref="Key"/> and <see cref="Ref"/>, this is not a non-attribute frame decoration. It
    /// lowers to an ordinary attribute whose name carries the event, which is what the framework's own
    /// <c>AddEventPreventDefaultAttribute</c> writes, and it is emitted inside the element's attribute
    /// range whatever order the chain was written in, because a reference capture closes that range
    /// (<c>ARCHITECTURE.md</c> §2.7).
    /// </para>
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView PreventDefault(this ElementView element) => element;

    /// <summary>
    /// Design-time syntax deciding at runtime whether the preceding event calls <c>preventDefault()</c>.
    /// Equivalent to <see cref="PreventDefault(ElementView)"/> when <paramref name="value"/> is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// A <see langword="false"/> emits the call and consumes a sequence number, and the framework then
    /// appends no frame — the same trade <see cref="Attr(ElementView, string, bool)"/> makes. Writing
    /// nothing at all is what emits nothing.
    /// </remarks>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">Whether the event prevents its default; any bool expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView PreventDefault(this ElementView element, bool value) => element;

    /// <summary>
    /// Design-time syntax making the preceding event call <c>stopPropagation()</c> in the browser, which is
    /// Razor's <c>@onwheel:stopPropagation</c>. Attaches and reports exactly as
    /// <see cref="PreventDefault(ElementView)"/> does.
    /// </summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView StopPropagation(this ElementView element) => element;

    /// <summary>
    /// Design-time syntax deciding at runtime whether the preceding event stops propagating; see
    /// <see cref="PreventDefault(ElementView, bool)"/> for what a <see langword="false"/> costs.
    /// </summary>
    /// <param name="element">The element being decorated (<c>Div</c>, <c>Span</c>, <c>Element("…")</c>, …).</param>
    /// <param name="value">Whether the event stops propagating; any bool expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView StopPropagation(this ElementView element, bool value) => element;
}

namespace BlazorCodeFirst;

/// <summary>
/// Standard-derived attribute shortcuts for <see cref="ElementView"/>: one member per HTML Living
/// Standard <c>Index — Attributes</c> entry that spells its name verbatim, has a determinate
/// string-or-boolean value shape, and emits exactly one attribute frame (ARCHITECTURE.md B.21
/// revisited, #490). None of these restrict which element they may be written on -- the same
/// choice <c>Href</c>/<c>Src</c>/<c>Alt</c> already made in <see cref="Decorations"/> -- so a
/// name here is sugar over <see cref="Decorations.Attr(ElementView, string, string)"/> or its
/// <see langword="bool"/> overload, never a new capability.
/// </summary>
public static partial class Decorations
{
    /// <summary>Design-time syntax setting the <c>abbr</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Abbr(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>accept</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Accept(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>accept-charset</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView AcceptCharset(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>accesskey</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Accesskey(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>action</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Action(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>allow</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Allow(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>allowfullscreen</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Allowfullscreen(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>alpha</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Alpha(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>as</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView As(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>async</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Async(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>autocapitalize</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Autocapitalize(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>autocomplete</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Autocomplete(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>autocorrect</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Autocorrect(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>autofocus</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Autofocus(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>autoplay</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Autoplay(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>blocking</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Blocking(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>charset</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Charset(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>checked</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Checked(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>cite</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Cite(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>closedby</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Closedby(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>color</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Color(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>colorspace</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Colorspace(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>cols</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Cols(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>colspan</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Colspan(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>command</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Command(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>commandfor</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Commandfor(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>content</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Content(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>contenteditable</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Contenteditable(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>controls</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Controls(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>coords</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Coords(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>crossorigin</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Crossorigin(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>data</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Data(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>datetime</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Datetime(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>decoding</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Decoding(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>default</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Default(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>defer</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Defer(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>dir</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Dir(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>dirname</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Dirname(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>disabled</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Disabled(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>download</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Download(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>draggable</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Draggable(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>enctype</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Enctype(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>enterkeyhint</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Enterkeyhint(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>fetchpriority</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Fetchpriority(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>for</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView For(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>form</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Form(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>formaction</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Formaction(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>formenctype</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Formenctype(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>formmethod</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Formmethod(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>formnovalidate</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Formnovalidate(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>formtarget</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Formtarget(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>headers</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Headers(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>headingoffset</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Headingoffset(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>headingreset</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Headingreset(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>height</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Height(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>hidden</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Hidden(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>high</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView High(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>hreflang</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Hreflang(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>http-equiv</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView HttpEquiv(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>imagesizes</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Imagesizes(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>imagesrcset</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Imagesrcset(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>inert</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Inert(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>inputmode</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Inputmode(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>integrity</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Integrity(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>is</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Is(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>ismap</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Ismap(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>itemid</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Itemid(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>itemprop</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Itemprop(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>itemref</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Itemref(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>itemscope</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Itemscope(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>itemtype</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Itemtype(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>kind</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Kind(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>label</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Label(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>lang</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Lang(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>list</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView List(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>loading</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Loading(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>loop</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Loop(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>low</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Low(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>max</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Max(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>maxlength</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Maxlength(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>media</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Media(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>method</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Method(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>min</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Min(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>minlength</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Minlength(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>multiple</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Multiple(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>muted</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Muted(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>name</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Name(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>nomodule</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Nomodule(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>nonce</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Nonce(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>novalidate</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Novalidate(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>open</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Open(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>optimum</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Optimum(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>pattern</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Pattern(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>ping</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Ping(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>placeholder</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Placeholder(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>playsinline</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Playsinline(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>popover</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Popover(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>popovertarget</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Popovertarget(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>popovertargetaction</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Popovertargetaction(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>poster</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Poster(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>preload</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Preload(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>readonly</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Readonly(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>referrerpolicy</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Referrerpolicy(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>rel</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Rel(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>required</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Required(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>reversed</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Reversed(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>rows</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Rows(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>rowspan</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Rowspan(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>sandbox</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Sandbox(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>scope</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Scope(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>selected</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Selected(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>shadowrootclonable</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Shadowrootclonable(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>shadowrootcustomelementregistry</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Shadowrootcustomelementregistry(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>shadowrootdelegatesfocus</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Shadowrootdelegatesfocus(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>shadowrootmode</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Shadowrootmode(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>shadowrootserializable</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">Blazor's conditional-attribute spelling: <see langword="true"/> writes the
    /// attribute with an empty value, <see langword="false"/> omits it.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Shadowrootserializable(this ElementView element, bool value) => element;

    /// <summary>Design-time syntax setting the <c>shadowrootslotassignment</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Shadowrootslotassignment(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>shape</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Shape(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>size</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Size(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>sizes</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Sizes(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>slot</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Slot(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>span</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Span(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>spellcheck</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Spellcheck(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>srcdoc</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Srcdoc(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>srclang</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Srclang(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>srcset</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Srcset(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>start</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Start(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>step</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Step(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>tabindex</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Tabindex(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>target</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Target(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>translate</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Translate(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>usemap</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Usemap(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>value</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Value(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>width</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Width(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>wrap</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Wrap(this ElementView element, string? value) => element;

    /// <summary>Design-time syntax setting the <c>writingsuggestions</c> attribute.</summary>
    /// <include file="Decorations.doc.xml" path="doc/fragment[@id='element']/param"/>
    /// <param name="value">The attribute value; any string expression.</param>
    /// <returns>The same inert receiver; never evaluated at runtime.</returns>
    public static ElementView Writingsuggestions(this ElementView element, string? value) => element;
}

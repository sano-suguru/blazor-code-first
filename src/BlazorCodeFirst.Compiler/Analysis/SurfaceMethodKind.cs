namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Which method of the design-time surface a symbol is, answered once by
/// <see cref="KnownSymbols.ClassifySurfaceMethod"/> and dispatched on by everything that reads the
/// surface.
/// </summary>
/// <remarks>
/// <para>
/// One enum across every group rather than one per group, because the sites that ask this question —
/// the failure scanner's recognition test, the scanner's own dispatch, and
/// <c>RenderExpressionAnalyzer</c>'s — used to answer it with three copies of one predicate chain
/// written in one order. A member missing from a copy failed silently: the scanner stopped recognizing
/// the method and the diagnostic it exists to produce went quiet, which is how #191 happened.
/// </para>
/// <para>
/// <c>[ViewPart]</c> has no member here. It is an attribute on a user method rather than a symbol
/// resolved out of the referenced runtime, so it cannot be answered from a table built at construction;
/// it stays a separate test beside the lookup, reached under <see cref="None"/>.
/// </para>
/// </remarks>
internal enum SurfaceMethodKind
{
    /// <summary>Not a method of the surface. A <c>[ViewPart]</c> call also answers this.</summary>
    None,

    /// <summary><c>Html.Element(tag)</c>, the escape hatch for a tag outside the curated table.</summary>
    Element,

    /// <summary><c>Html.If(condition, then, otherwise)</c>.</summary>
    If,

    /// <summary><c>Html.ForEach&lt;T&gt;(source, key, content)</c>.</summary>
    ForEach,

    /// <summary><c>Html.Component&lt;T&gt;()</c>.</summary>
    Component,

    /// <summary><c>Html.Raw(markup)</c>.</summary>
    Raw,

    /// <summary><c>Html.Fragment(children)</c>.</summary>
    Fragment,

    /// <summary><c>ComponentView&lt;T&gt;.Param(selector, value)</c> for an ordinary parameter.</summary>
    ScalarParam,

    /// <summary><c>ComponentView&lt;T&gt;.Param(selector, view)</c> for a <c>RenderFragment</c> slot.</summary>
    FragmentParam,

    /// <summary>
    /// <c>ComponentView&lt;T&gt;.Template&lt;TContext&gt;(selector, view)</c>, the spelling that does not
    /// name the context.
    /// </summary>
    GenericTemplateIgnored,

    /// <summary>
    /// <c>ComponentView&lt;T&gt;.Template&lt;TContext&gt;(selector, context =&gt; view)</c>, the spelling
    /// that names it.
    /// </summary>
    GenericTemplateContextual,

    /// <summary><c>ComponentView&lt;T&gt;.Bind&lt;TValue&gt;(selector, get[, set])</c>.</summary>
    ComponentBind,

    /// <summary><c>Decorations.Class(this ElementView, string)</c>.</summary>
    Class,

    /// <summary>
    /// A named attribute shortcut, <c>.Href</c> and its siblings. The attribute it stands for is read from
    /// <see cref="KnownSymbols.AttributeShortcuts"/>.
    /// </summary>
    AttributeShortcut,

    /// <summary>
    /// A named event shortcut, <c>.OnClick</c>. The event it stands for is read from
    /// <see cref="KnownSymbols.EventShortcuts"/>.
    /// </summary>
    EventShortcut,

    /// <summary><c>Decorations.Attr</c>, either overload.</summary>
    Attr,

    /// <summary><c>Decorations.On</c>, any overload.</summary>
    On,

    /// <summary><c>Decorations.Bind</c>, any overload.</summary>
    Bind,

    /// <summary>
    /// <c>Decorations.Key(this ElementView, object?)</c>. A decoration that is not an attribute: it lowers
    /// to <c>SetKey</c> rather than to an attribute frame (<c>ARCHITECTURE.md</c> §2.7(E)).
    /// </summary>
    Key,

    /// <summary>
    /// <c>ComponentView&lt;T&gt;.Key(object?)</c>. The same channel as <see cref="Key"/> on the same
    /// <c>SetKey</c>, and a member of its own because the receiver decides which node the classification
    /// arm has to reach for: an <c>ElementTemplateNode</c> there, a <c>ComponentTemplateNode</c> here.
    /// </summary>
    ComponentKey,

    /// <summary>
    /// <c>ComponentView&lt;T&gt;.RenderMode(IComponentRenderMode?)</c>. A non-attribute frame decoration
    /// like the two above, and the only one with no element counterpart: a render mode belongs to a
    /// component frame and there is nothing for it to mean on an element.
    /// </summary>
    ComponentRenderMode,

    /// <summary>
    /// <c>Decorations.Ref(this ElementView, Action&lt;ElementReference&gt;)</c>. A non-attribute frame
    /// decoration that, unlike <see cref="Key"/>, appends a frame and so consumes a sequence number
    /// (<c>ARCHITECTURE.md</c> §2.7(E)).
    /// </summary>
    Ref,

    /// <summary>
    /// <c>ComponentView&lt;T&gt;.Ref(Action&lt;T&gt;)</c>. The same channel as <see cref="Ref"/> on a
    /// different builder call, split for the reason <see cref="ComponentKey"/> is split from
    /// <see cref="Key"/>.
    /// </summary>
    ComponentRef,

    /// <summary>
    /// <c>Decorations.FormName(this ElementView, string)</c>. A non-attribute frame decoration that,
    /// like <see cref="ComponentRenderMode"/>, consumes no sequence number but does stack a frame
    /// (<c>ARCHITECTURE.md</c> §2.7(E)). The only one with no component counterpart in the other
    /// direction: <c>AddNamedEvent</c> requires the current parent frame to be an element.
    /// </summary>
    FormName,

    /// <summary>
    /// <c>Decorations.PreventDefault</c>, either overload. Unlike the three above it is an ordinary
    /// attribute frame: it lowers to an <c>AddAttribute</c> whose name carries the event it attaches to,
    /// so it lives inside the attribute range rather than after it (<c>ARCHITECTURE.md</c> §2.7).
    /// </summary>
    PreventDefault,

    /// <summary><c>Decorations.StopPropagation</c>, either overload; see <see cref="PreventDefault"/>.</summary>
    StopPropagation,

    /// <summary>
    /// <c>Decorations.Attrs(this ElementView, IReadOnlyDictionary&lt;string, object&gt;?)</c>. Not an
    /// ordinary attribute: it lowers to <c>AddMultipleAttributes</c>, a single-field channel like
    /// <see cref="Key"/>/<see cref="Ref"/>/<see cref="FormName"/> rather than a repeatable one like
    /// <see cref="Attr"/> (<c>ARCHITECTURE.md</c> Appendix B.14, revised #387).
    /// </summary>
    AttributesSplat,

    /// <summary>
    /// <c>Decorations.Class&lt;TComponent&gt;(this ComponentView&lt;TComponent&gt;, string?)</c>. Sugar
    /// for <c>.Attr("class", value)</c> on a component call — no class-channel folding on this
    /// receiver (#314's scope is a single constant attribute, not concatenation); two class-shaped
    /// decorations on one component call is BCF3010, the same as any other duplicate name.
    /// </summary>
    ComponentClass,

    /// <summary>
    /// <c>Decorations.Attr&lt;TComponent&gt;(this ComponentView&lt;TComponent&gt;, string, ...)</c>, any
    /// overload. Emits a plain <c>AddAttribute</c> before every <c>AddComponentParameter</c>, which
    /// Blazor routes into the callee's <c>[Parameter(CaptureUnmatchedValues = true)]</c> dictionary
    /// when the name matches no declared parameter (#314). A name that does match, case-insensitively
    /// (measured — Blazor's own matching is case-insensitive), is rejected as BCF3042 rather than
    /// silently binding the parameter and bypassing <c>.Param</c>'s type checking.
    /// </summary>
    ComponentAttr,

    /// <summary>
    /// <c>ComponentView&lt;TComponent&gt;.Id</c>/<c>.Type</c>/<c>.Title</c>/<c>.Role</c>/<c>.Href</c>/
    /// <c>.Src</c>/<c>.Alt</c> (#489) — the same seven names as the element-side
    /// <see cref="AttributeShortcut"/>, declared as members for the reason <see cref="ComponentClass"/>
    /// is. Each is sugar for <see cref="ComponentAttr"/> with the name fixed by which member was called,
    /// read from <see cref="KnownSymbols.ComponentAttributeShortcuts"/>, and carries the same BCF3042
    /// collision guard.
    /// </summary>
    ComponentAttributeShortcut,

    /// <summary>
    /// <c>ComponentView&lt;TComponent&gt;.Param(selector, handler)</c> for an <c>EventCallback</c>- or
    /// <c>EventCallback&lt;TArg&gt;</c>-typed parameter (#492): the four overloads taking
    /// <c>Action</c>/<c>Func&lt;Task&gt;</c>/<c>Action&lt;TArg&gt;</c>/<c>Func&lt;TArg, Task&gt;</c>,
    /// which the generator wraps in <c>EventCallback.Factory.Create</c> the way <c>.On</c> and
    /// <c>.Bind</c>'s derived <c>{name}Changed</c> already are — unlike <see cref="ScalarParam"/>, whose
    /// value is cast and passed through verbatim.
    /// </summary>
    ComponentParamEventCallback,
}

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
}

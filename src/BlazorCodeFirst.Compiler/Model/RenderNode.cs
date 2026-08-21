using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler;

/// <summary>
/// Symbol-free, value-equal capture of a source location for a template-phase node. Stores only the
/// primitive coordinates required to reconstruct a <see cref="Location"/> at diagnostic-report time,
/// following the same discipline as <see cref="Diagnostics.DiagnosticInfo"/>.
/// </summary>
internal readonly record struct TemplateLocation(
    string FilePath,
    TextSpan Span,
    LinePositionSpan LineSpan)
{
    public static TemplateLocation From(Location location)
    {
        var lineSpan = location.GetLineSpan();
        return new TemplateLocation(lineSpan.Path ?? string.Empty, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

/// <summary>
/// One argument of a <c>[ViewPart]</c> call, in the callee's parameter order, carrying the source order
/// that expansion evaluates the arguments in.
/// </summary>
/// <remarks>
/// The parameter's type name is deliberately absent: expansion declares each local from the
/// <em>definition</em>'s <see cref="Analysis.ViewPartParameter.TypeName"/>, so a per-argument copy was
/// written at every call site and read nowhere. The only place the callee's type name is still needed is
/// an omitted optional, where <c>ConstantTemplate.ForParameterDefault</c> spells the default's cast, and
/// that is computed in the branch that uses it.
/// </remarks>
internal sealed record ViewPartInvocationArgument(
    int ParameterOrdinal,
    int SourceOrder,
    bool IsImplicitDefault,
    ExpressionTemplate Value);

/// <summary>
/// One <c>View</c>-typed argument of a <c>[ViewPart]</c> call: an additional content slot, bound to the
/// callee's parameter ordinal (#34).
/// </summary>
/// <remarks>
/// A channel separate from <see cref="ViewPartInvocationArgument"/>, for the reason
/// <see cref="ComponentSlotNode"/> is separate from <see cref="ComponentParameter"/>: the content is a node
/// tree, and it takes part in hole substitution, sequence allocation, and expansion, none of which are
/// defined over <see cref="ExpressionTemplate"/>. It also carries no source order, because there is no
/// local to bind and therefore no evaluation to order — the subtree is spliced where the callee names it.
/// </remarks>
internal sealed record ViewPartContentArgument(int ParameterOrdinal, RenderNode Content);

/// <summary>
/// Discriminated union of statically sequenceable UI nodes, spanning both pipeline phases: as built from
/// a design-time expression (<c>Body</c> or <c>Chrome</c>) before <c>[ViewPart]</c> expansion, and as
/// produced by <see cref="Generation.ViewPartExpander"/> afterward. All fields contain only immutable
/// templates and primitive values so that instances are immutable and value-equal.
/// </summary>
/// <remarks>
/// A field whose value is only known after expansion (<see cref="ElementNode.CssScope"/>,
/// <see cref="ForEachNode.LoopVariableName"/>, <see cref="ComponentSlotNode.ContextVariableName"/>) is
/// <see langword="null"/> before it. <see cref="ForEachNode.Location"/> runs the other way: populated
/// before expansion for BCF3002/BCF3003, explicitly cleared to <see langword="null"/> by
/// <see cref="Generation.ViewPartExpander"/> once expanded, because no expanded node may carry an absolute
/// <see cref="TextSpan"/> — doing so would tie the incremental generator's cache to source positions a
/// whitespace-only edit changes. <see cref="ContentHoleNode"/> and <see cref="ViewPartCallNode"/> are
/// template-phase only: expansion always replaces or removes them, so neither reaches
/// <see cref="RenderViewEmitter"/> or <see cref="Generation.StaticMarkupSerializer"/> — both close their
/// dispatch switches with <c>default: throw</c>, which is what stands in for a compile-time phase
/// boundary now that one type spans both phases.
/// </remarks>
internal abstract record RenderNode;

/// <summary>
/// A hole where a <c>[ViewPart]</c> body names content its caller supplies: <c>Html.Slot</c>, or a
/// reference to one of the definition's own <c>View</c>-typed parameters. Expansion replaces it with the
/// argument's own subtree, so it never reaches <see cref="RenderViewEmitter"/>.
/// </summary>
/// <remarks>
/// One node type for both spellings because they are the same thing at different ordinals: the bracket
/// content is bound at <see cref="Analysis.ViewPartDefinition.SlotOrdinal"/> and a <c>View</c> parameter
/// at its own, and both are indices into the one substitution the expander carries.
/// </remarks>
internal sealed record ContentHoleNode(int ParameterOrdinal) : RenderNode;

/// <summary>Represents an <c>If(condition, then, otherwise)</c> call with an optional else branch.</summary>
internal sealed record IfNode(ExpressionTemplate ConditionExpression, RenderNode Then, RenderNode? Otherwise) : RenderNode;

/// <summary>
/// A native `if`/`else` statement transplanted whole (ARCHITECTURE.md §5.3), as opposed to
/// <see cref="IfNode"/>'s `If()` combinator. Same shape as <see cref="IfNode"/> deliberately: what differs
/// is emission, not structure — see <c>EmitTransplantedIf</c>. <see cref="Then"/>/<see cref="Otherwise"/>
/// may themselves be a <see cref="TransplantedBlockNode"/> (an arm with leading statements) wrapping either
/// an ordinary content node or a further nested <see cref="TransplantedIfNode"/> (an `else if`).
/// </summary>
internal sealed record TransplantedIfNode(
    ExpressionTemplate Condition, RenderNode Then, RenderNode? Otherwise) : RenderNode;

internal sealed record LocalBinding(
    string TypeName,
    string Name,
    ExpressionTemplate Initializer);

internal sealed record ExpansionNode(
    EquatableArray<LocalBinding> Locals,
    RenderNode Body) : RenderNode;

/// <summary>A call the generator statically expands: a <c>[ViewPart]</c> invocation before inlining.</summary>
/// <remarks>
/// Never reaches <see cref="RenderViewEmitter"/>: <see cref="Generation.ViewPartExpander"/> always either
/// replaces it with <see cref="ExpansionNode"/> or fails expansion (BCF1002).
/// </remarks>
internal sealed record ViewPartCallNode(
    string MethodKey,
    string DisplayName,
    EquatableArray<ViewPartInvocationArgument> Arguments,
    TemplateLocation Location) : RenderNode
{
    /// <summary>
    /// Every piece of content this call supplies, bound to the callee's ordinals: one entry per
    /// <c>View</c>-typed argument, plus the bracket content at the slot ordinal when the call has brackets
    /// (#34, #176).
    /// </summary>
    /// <remarks>
    /// One channel and not two. The bracket is not an argument, but it is content bound to an ordinal exactly
    /// as an argument's is, and the site that builds this node has the callee's symbol in hand, so it can
    /// name the slot ordinal itself rather than leaving the expander to reconcile two transports. That
    /// reconciliation was where the mismatch guards came from.
    /// </remarks>
    public EquatableArray<ViewPartContentArgument> ContentArguments { get; init; }
}

/// <summary>
/// Represents a <c>ForEach(source, key, content)</c> call. Emits a <c>foreach</c> region: the content
/// template occupies one static sequence space reused every iteration, and <see cref="LoopVariableName"/>
/// is the generated iteration variable that content/key expressions were substituted onto.
/// </summary>
/// <param name="Source">The expression template producing the sequence iterated.</param>
/// <param name="Key">
/// The key expression applied to the content root with <c>SetKey</c>, or <see langword="null"/> when the
/// author declined the key (#172). A declined key also lets the content root fold, because the fold check
/// in <c>RenderViewEmitter.EmitNode</c> turns on a threaded key and nothing else.
/// </param>
/// <param name="Content">The loop body: template before expansion, expanded subtree after.</param>
/// <param name="Location">
/// The <c>ForEach</c> call's source location, blamed for BCF3002/BCF3003. Populated by construction,
/// cleared to <see langword="null"/> by <see cref="Generation.ViewPartExpander"/> once expanded (see
/// <see cref="RenderNode"/>'s remarks).
/// </param>
/// <param name="LoopVariableName">
/// The generated iteration variable name <paramref name="Content"/> and <paramref name="Key"/> were
/// substituted onto, or <see langword="null"/> before expansion.
/// </param>
internal sealed record ForEachNode(
    ExpressionTemplate Source,
    ExpressionTemplate? Key,
    RenderNode Content,
    TemplateLocation? Location,
    string? LoopVariableName = null) : RenderNode;

internal enum ComponentSlotKind
{
    NonGeneric,
    GenericContextIgnored,
    GenericContextual,
}

/// <summary>A single statically-bound component parameter: its name and value expression template.</summary>
/// <param name="Name">The component parameter's name.</param>
/// <param name="Value">The expression template bound to the parameter.</param>
/// <param name="ValueTypeName">
/// The type <c>.Param</c> resolved its value to, fully qualified, or <see langword="null"/> where none can
/// be written and for the parameters <c>.Bind</c> composes, which carry their own types already. The
/// emitter casts the value to it. <c>AddComponentParameter</c> takes <c>object?</c>, so a value that had a
/// target type at the call site arrives with none: an expression with no natural type does not bind at all,
/// and one whose natural type is not the declared type binds and is the wrong type at render (#377).
/// </param>
internal sealed record ComponentParameter(
    string Name,
    ExpressionTemplate Value,
    string? ValueTypeName = null);

/// <summary>A RenderFragment-typed component parameter: template before expansion, expanded subtree after.</summary>
/// <remarks>
/// Kept in a channel separate from <see cref="ComponentParameter"/> because the content is a node tree: it
/// takes part in hole substitution, sequence allocation, and <c>[ViewPart]</c> expansion, none of which are
/// defined over <see cref="ExpressionTemplate"/>. <see cref="ContextVariableName"/> is set only for a
/// <see cref="ComponentSlotKind.GenericContextual"/> slot, and only after expansion mints it.
/// </remarks>
internal sealed record ComponentSlotNode(string Name, RenderNode Content)
{
    public ComponentSlotKind Kind { get; init; }
    public string? ContextTypeName { get; init; }
    public string? ContextVariableName { get; init; }
}

/// <summary>
/// Represents a <c>Component&lt;T&gt;()</c> parameter chain. Emits <c>OpenComponent&lt;T&gt;</c> followed by
/// one <c>AddComponentParameter</c> per scalar parameter (in source order), then one
/// <c>AddComponentParameter</c> per fragment slot carrying a statically sequenced lambda, and
/// <c>CloseComponent</c>. <see cref="TypeName"/> is the fully qualified component type (already prefixed
/// with <c>global::</c>).
/// </summary>
internal sealed record ComponentNode(
    string TypeName,
    EquatableArray<ComponentParameter> Parameters,
    EquatableArray<ComponentSlotNode> Slots = default,
    // HTML attribute names written with .Attr/.Class (#314), one frame each, emitted before every
    // AddComponentParameter call. A separate name space from Parameters/Slots above: an attribute
    // name colliding with a declared parameter name is rejected earlier, at classification time, as
    // BCF3042, so nothing here is ever checked against Parameters/Slots — only against itself
    // (BCF3010, a name written twice).
    EquatableArray<AttributeTemplate> Attributes = default) : RenderNode
{
    /// <summary>The key written with <c>.Key</c>, or <see langword="null"/>. Emitted as <c>SetKey</c>
    /// immediately after the component opens, consuming no sequence number (§2.7(E)).</summary>
    public ExpressionTemplate? Key { get; init; }

    /// <summary>The render mode written with <c>.RenderMode</c>, or <see langword="null"/>. Emitted after
    /// the parameter frames, consuming no sequence number (§2.7(E)).</summary>
    public ExpressionTemplate? RenderMode { get; init; }

    /// <summary>The capture action written with <c>.Ref</c>, or <see langword="null"/>. Emitted as
    /// <c>AddComponentReferenceCapture</c> after the render mode frame, consuming one sequence number
    /// (§2.7(E)).</summary>
    public ExpressionTemplate? Ref { get; init; }
}

/// <param name="Name">The event's attribute name, <c>on</c> prefix included.</param>
/// <param name="Handler">The handler expression, lowered to an <c>EventCallback</c> at emission.</param>
/// <param name="ArgsTypeName">
/// The argument type <c>.On&lt;TArgs&gt;</c> resolved to, fully qualified, or <see langword="null"/> for
/// the overloads that carry no type argument at all. The emitter writes it onto
/// <c>EventCallback.Factory.Create</c>, taking inference out of the picture (#371).
/// </param>
/// <param name="PreventDefault">
/// The <c>preventDefault</c> modifier's value, or <see langword="null"/> when no modifier was written on
/// this event. Null is not the same as a written <c>false</c> (#368).
/// </param>
/// <param name="StopPropagation">The <c>stopPropagation</c> modifier's value; see PreventDefault.</param>
internal sealed record EventTemplate(
    string Name,
    ExpressionTemplate Handler,
    string? ArgsTypeName = null,
    ExpressionTemplate? PreventDefault = null,
    ExpressionTemplate? StopPropagation = null);

/// <summary>An element attribute: a resolved constant name plus a value expression template.</summary>
internal sealed record AttributeTemplate(string Name, ExpressionTemplate Value);

/// <summary>
/// A two-way binding on an element: the attribute carrying the current value, the event writing it back,
/// the value expression, and the facts the binder call is assembled from.
/// </summary>
/// <param name="AttributeName">The attribute carrying the current value.</param>
/// <param name="EventName">The event writing the value back.</param>
/// <param name="Value">The bound value expression, possibly still holding an unbound parameter hole in a <c>[ViewPart]</c> body.</param>
/// <param name="ValueTypeName">The bound value's type, fully qualified with no special-type spellings.</param>
/// <param name="Setter">The author's own setter, or <see langword="null"/> for the getter-only form.</param>
/// <param name="SetterIsAsynchronous">Whether <paramref name="Setter"/> returns something other than <see langword="void"/>.</param>
/// <param name="Culture">The culture expression, or <see langword="null"/> for the overloads that take none.</param>
/// <param name="Format">The format expression, or <see langword="null"/>.</param>
/// <param name="PreventDefault">The <c>preventDefault</c> modifier's value written on this binding's own event.</param>
/// <param name="StopPropagation">The <c>stopPropagation</c> half; see PreventDefault.</param>
internal sealed record BindTemplate(
    string AttributeName,
    string EventName,
    ExpressionTemplate Value,
    string ValueTypeName,
    ExpressionTemplate? Setter,
    bool SetterIsAsynchronous,
    ExpressionTemplate? Culture = null,
    ExpressionTemplate? Format = null,
    ExpressionTemplate? PreventDefault = null,
    ExpressionTemplate? StopPropagation = null);

/// <summary>An HTML element: tag, folded class channel, attributes, event list, and mixed children.</summary>
internal sealed record ElementNode(
    string Tag,
    EquatableArray<ExpressionTemplate> Classes = default,
    EquatableArray<AttributeTemplate> Attributes = default,
    EquatableArray<EventTemplate> Events = default,
    EquatableArray<RenderNode> Children = default) : RenderNode
{
    /// <summary>The element's two-way bindings in source order. Two frames each: the attribute, then the event.</summary>
    public EquatableArray<BindTemplate> Bindings { get; init; }

    /// <summary>The key written with <c>.Key</c>, or <see langword="null"/>. Stops the fold (§2.7(E)).</summary>
    public ExpressionTemplate? Key { get; init; }

    /// <summary>The capture action written with <c>.Ref</c>, or <see langword="null"/>. Stops the fold (§2.7(E)).</summary>
    public ExpressionTemplate? Ref { get; init; }

    /// <summary>The form name written with <c>.FormName</c>, or <see langword="null"/>. Stops the fold (§2.7(E)).</summary>
    public ExpressionTemplate? FormName { get; init; }

    /// <summary>The dictionary written with <c>.Attrs</c>, or <see langword="null"/>. Stops the fold (§2.7(E)).</summary>
    public ExpressionTemplate? AttributesSplat { get; init; }

    /// <summary>
    /// The scoped-CSS attribute value (<c>bcf-xxxxxxxx</c>) this element's owning file carries, or
    /// <see langword="null"/> before expansion, or after it when that file has no matching <c>.cs.css</c>.
    /// </summary>
    public string? CssScope { get; init; }
}

/// <summary>A bare text node emitted with AddContent (no wrapping element).</summary>
internal sealed record TextContentNode(ExpressionTemplate Content) : RenderNode;

/// <summary>A wrapper-less group: mixed children emitted in sequence with no enclosing element.</summary>
internal sealed record FragmentNode(EquatableArray<RenderNode> Children = default) : RenderNode;

/// <summary>Trusted raw HTML emitted with AddMarkupContent (no wrapping element).</summary>
internal sealed record RawMarkupNode(ExpressionTemplate Content) : RenderNode;

/// <summary>
/// An externally supplied <c>RenderFragment</c> placed with AddContent (no wrapping element). Blazor wraps
/// the fragment in a region so its internal sequence numbers stay isolated from ours; that region frame is
/// emitted only when the fragment is non-null, but the AddContent call itself is unconditional, so the
/// width is always 1.
/// </summary>
internal sealed record RenderFragmentContentNode(ExpressionTemplate Content) : RenderNode;

/// <summary>
/// A call the generator cannot expand statically, rendered at runtime through the <c>RenderFragment</c>
/// the returned <c>View</c> carries (ARCHITECTURE.md §2.3 Opaque, §3.2). Opens no keyable frame, so
/// <see cref="Analysis.KeyabilityResolver"/> answers <see cref="Analysis.ContentRootKind.Region"/> for it
/// and BCF3003 rejects it as a <c>ForEach</c> content root — the same answer
/// <see cref="RenderFragmentContentNode"/> gets, and for the same reason.
/// </summary>
internal sealed record OpaqueViewNode(ExpressionTemplate Call) : RenderNode;

/// <summary>
/// One generated scope: the names expansion mints for it (<paramref name="LocalCount"/>), and the
/// statements transplanted ahead of the content they lead into (ARCHITECTURE.md §2.3 Transplantable).
/// Either half may be absent, and a body that declares only through its expression has no statements at
/// all. The statements consume no sequence numbers, so the wrapped content keeps the width it has on its
/// own.
/// </summary>
/// <remarks>
/// Structurally the same shape as <c>ExpansionNode</c> — bindings, then a body that still owns the key —
/// and kept separate because the bindings differ: an expansion declares typed locals the expander built,
/// this carries statements the author wrote.
/// </remarks>
/// <param name="Statements">The block's statements, transplanted verbatim ahead of the returned expression.</param>
/// <param name="Content">The returned expression: template before expansion, expanded subtree after.</param>
/// <param name="LocalCount">
/// How many locals this body declares as render variables, which is how many names expansion mints for it
/// (#336). Zero for a component's own expression, whose authored names stand as written. Unused after
/// expansion but kept rather than cleared: unlike <see cref="TemplateLocation"/>, an <see langword="int"/>
/// carries no source position and so raises no incremental-caching concern.
/// </param>
internal sealed record TransplantedBlockNode(
    ExpressionTemplate Statements, RenderNode Content, int LocalCount = 0) : RenderNode;

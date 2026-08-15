namespace BlazorCodeFirst.Compiler;

/// <summary>
/// Discriminated union of statically sequenceable UI nodes extracted from a design-time expression
/// (<c>Body</c> or <c>Chrome</c>).
/// All fields contain only immutable templates and primitive values so that instances are immutable and value-equal.
/// No syntax nodes, symbols, semantic models, or absolute TextSpan offsets are stored here.
/// </summary>
internal abstract record RenderNode;

/// <summary>Represents an <c>If(condition, then, otherwise)</c> call with an optional else branch.</summary>
internal sealed record IfNode(ExpressionTemplate ConditionExpression, RenderNode Then, RenderNode? Otherwise) : RenderNode;

internal sealed record LocalBinding(
    string TypeName,
    string Name,
    ExpressionTemplate Initializer);

internal sealed record ExpansionNode(
    EquatableArray<LocalBinding> Locals,
    RenderNode Body) : RenderNode;

/// <summary>
/// Represents a <c>ForEach(source, key, content)</c> call. Emits a <c>foreach</c> region:
/// the content template occupies one static sequence space reused every iteration, and
/// <see cref="LoopVariableName"/> is the generated iteration variable that content/key expressions
/// were substituted onto.
/// </summary>
/// <param name="Key">
/// The key expression applied to the content root with <c>SetKey</c>, or <see langword="null"/> when the
/// author declined the key (#172). A declined key also lets the content root fold, because the fold check
/// in <c>RenderViewEmitter.EmitNode</c> turns on a threaded key and nothing else.
/// </param>
internal sealed record ForEachNode(
    ExpressionTemplate Source,
    ExpressionTemplate? Key,
    RenderNode Content,
    string LoopVariableName) : RenderNode;

/// <summary>A single statically-bound component parameter: its name and value expression template.</summary>
/// <remarks>Shared by <see cref="ComponentTemplateNode"/> (holes intact) and <see cref="ComponentNode"/>
/// (holes substituted). Symbol-free and value-equal.</remarks>
internal sealed record ComponentParameter(string Name, ExpressionTemplate Value);

/// <summary>A RenderFragment-typed component parameter whose content is an expanded node subtree.</summary>
/// <remarks>
/// Expanded counterpart of <see cref="ComponentSlot"/> (holes substituted). Contextual generic slots
/// carry the deterministic generated lambda parameter name; all metadata remains primitive/string data.
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
/// <c>CloseComponent</c>.
/// <see cref="TypeName"/> is the fully qualified component type (already prefixed with <c>global::</c>).
/// </summary>
internal sealed record ComponentNode(
    string TypeName,
    EquatableArray<ComponentParameter> Parameters,
    EquatableArray<ComponentSlotNode> Slots = default) : RenderNode
{
    /// <summary>
    /// The key written with <c>.Key</c>, or <see langword="null"/>. Expanded counterpart of
    /// <see cref="ComponentTemplateNode.Key"/>; emitted as <c>SetKey</c> immediately after the component
    /// opens, consuming no sequence number (§2.7(E)).
    /// </summary>
    public ExpressionTemplate? Key { get; init; }

    /// <summary>
    /// The render mode written with <c>.RenderMode</c>, or <see langword="null"/>. Expanded counterpart of
    /// <see cref="ComponentTemplateNode.RenderMode"/>; emitted after the parameter frames, consuming no
    /// sequence number (§2.7(E)).
    /// </summary>
    public ExpressionTemplate? RenderMode { get; init; }
}

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

    /// <summary>
    /// The key written with <c>.Key</c>, or <see langword="null"/>. Expanded counterpart of
    /// <see cref="ElementTemplateNode.Key"/>; emitted as <c>SetKey</c> immediately after the element opens,
    /// consuming no sequence number (§2.7(E)).
    /// </summary>
    public ExpressionTemplate? Key { get; init; }
}

/// <summary>A bare text node emitted with AddContent (no wrapping element).</summary>
internal sealed record TextContentNode(ExpressionTemplate Content) : RenderNode;

/// <summary>A wrapper-less group: mixed children emitted in sequence with no enclosing element.</summary>
internal sealed record FragmentNode(EquatableArray<RenderNode> Children = default) : RenderNode;

/// <summary>Trusted raw HTML emitted with AddMarkupContent (no wrapping element).</summary>
internal sealed record RawMarkupNode(ExpressionTemplate Content) : RenderNode;

/// <summary>
/// An externally supplied <c>RenderFragment</c> placed with AddContent (no wrapping element). Blazor
/// wraps the fragment in a region so its internal sequence numbers stay isolated from ours; that region
/// frame is emitted only when the fragment is non-null, but the AddContent call itself is unconditional,
/// so the width is always 1.
/// </summary>
internal sealed record RenderFragmentContentNode(ExpressionTemplate Content) : RenderNode;

/// <summary>
/// Expanded counterpart of <see cref="OpaqueViewTemplateNode"/>. Emits one
/// <c>AddContent(seq, RenderFragment?)</c> frame; Blazor opens the region for the fragment itself, so the
/// width is 1 and no OpenRegion is written here.
/// </summary>
internal sealed record OpaqueViewNode(ExpressionTemplate Call) : RenderNode;

/// <summary>Expanded counterpart of <see cref="TransplantedBlockTemplateNode"/>.</summary>
internal sealed record TransplantedBlockNode(
    ExpressionTemplate Statements, RenderNode Content) : RenderNode;

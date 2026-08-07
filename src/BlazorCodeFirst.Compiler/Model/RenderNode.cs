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
/// Represents a <c>ForEach(source, key, content)</c> call. Emits a keyed <c>foreach</c> region:
/// the content template occupies one static sequence space reused every iteration, and
/// <see cref="LoopVariableName"/> is the generated iteration variable that content/key expressions
/// were substituted onto.
/// </summary>
internal sealed record ForEachNode(
    ExpressionTemplate Source,
    ExpressionTemplate Key,
    RenderNode Content,
    string LoopVariableName) : RenderNode;

/// <summary>A single statically-bound component parameter: its name and value expression template.</summary>
/// <remarks>Shared by <see cref="ComponentTemplateNode"/> (holes intact) and <see cref="ComponentNode"/>
/// (holes substituted). Symbol-free and value-equal.</remarks>
internal sealed record ComponentParameter(string Name, ExpressionTemplate Value);

/// <summary>A RenderFragment-typed component parameter whose content is an expanded node subtree.</summary>
/// <remarks>Expanded counterpart of <see cref="ComponentSlot"/> (holes substituted).</remarks>
internal sealed record ComponentSlotNode(string Name, RenderNode Content);

/// <summary>
/// Represents a <c>Component&lt;T&gt;().Param(...)</c> call. Emits <c>OpenComponent&lt;T&gt;</c> followed by
/// one <c>AddComponentParameter</c> per scalar parameter (in source order), then one
/// <c>AddComponentParameter</c> per fragment slot carrying a statically sequenced lambda, and
/// <c>CloseComponent</c>.
/// <see cref="TypeName"/> is the fully qualified component type (already prefixed with <c>global::</c>).
/// </summary>
internal sealed record ComponentNode(
    string TypeName,
    EquatableArray<ComponentParameter> Parameters,
    EquatableArray<ComponentSlotNode> Slots = default) : RenderNode;

/// <summary>An HTML element: tag, folded class channel, attributes, event list, and mixed children.</summary>
internal sealed record ElementNode(
    string Tag,
    EquatableArray<ExpressionTemplate> Classes = default,
    EquatableArray<AttributeTemplate> Attributes = default,
    EquatableArray<EventTemplate> Events = default,
    EquatableArray<RenderNode> Children = default) : RenderNode
{
    /// <summary>The element's two-way binding, or null. Two frames: the attribute, then the event.</summary>
    public BindTemplate? Bind { get; init; }
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

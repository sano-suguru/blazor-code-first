namespace BlazorCompose.Compiler;

/// <summary>
/// Discriminated union of statically sequenceable UI nodes extracted from a <c>Body</c> expression.
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

/// <summary>
/// Represents a <c>Component&lt;T&gt;().Param(...)</c> call. Emits <c>OpenComponent&lt;T&gt;</c> followed by
/// one <c>AddComponentParameter</c> per parameter (in source order) and <c>CloseComponent</c>.
/// <see cref="TypeName"/> is the fully qualified component type (already prefixed with <c>global::</c>).
/// </summary>
internal sealed record ComponentNode(
    string TypeName,
    EquatableArray<ComponentParameter> Parameters) : RenderNode;

/// <summary>An HTML element: tag, folded class channel, event list, and mixed children (text or elements).</summary>
internal sealed record ElementNode(
    string Tag,
    EquatableArray<ExpressionTemplate> Classes = default,
    EquatableArray<EventTemplate> Events = default,
    EquatableArray<RenderNode> Children = default) : RenderNode;

/// <summary>A bare text node emitted with AddContent (no wrapping element).</summary>
internal sealed record TextContentNode(ExpressionTemplate Content) : RenderNode;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler;

/// <summary>
/// Symbol-free, value-equal capture of a source location for a template node. Stores only the
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

internal sealed record ComposableInvocationArgument(
    int ParameterOrdinal,
    int SourceOrder,
    string ParameterTypeName,
    bool IsImplicitDefault,
    ExpressionTemplate Value);

internal abstract record RenderTemplateNode;

internal sealed record IfTemplateNode(
    ExpressionTemplate Condition,
    RenderTemplateNode Then,
    RenderTemplateNode? Otherwise) : RenderTemplateNode;

internal sealed record ComposableCallTemplateNode(
    string MethodKey,
    string DisplayName,
    EquatableArray<ComposableInvocationArgument> Arguments,
    TemplateLocation Location) : RenderTemplateNode;

internal sealed record ForEachTemplateNode(
    ExpressionTemplate Source,
    ExpressionTemplate Key,
    RenderTemplateNode Content,
    TemplateLocation Location) : RenderTemplateNode;

internal enum ComponentSlotKind
{
    NonGeneric,
    GenericContextIgnored,
    GenericContextual,
}

/// <summary>
/// A RenderFragment-typed component parameter whose value is BlazorCodeFirst content rather than an expression.
/// Kept in a channel separate from <see cref="ComponentParameter"/> because the content is a node tree:
/// it takes part in hole substitution, sequence allocation, and [Composable] expansion, none of which are
/// defined over <see cref="ExpressionTemplate"/>.
/// </summary>
internal sealed record ComponentSlot(string Name, RenderTemplateNode Content)
{
    public ComponentSlotKind Kind { get; init; }
    public string? ContextTypeName { get; init; }
}

internal sealed record ComponentTemplateNode(
    string TypeName,
    EquatableArray<ComponentParameter> Parameters,
    EquatableArray<ComponentSlot> Slots = default) : RenderTemplateNode;

internal sealed record EventTemplate(string Name, ExpressionTemplate Handler);

/// <summary>An element attribute: a resolved constant name plus a value expression template.</summary>
internal sealed record AttributeTemplate(string Name, ExpressionTemplate Value);

/// <summary>
/// A two-way binding on an element: the attribute carrying the current value, the event writing it
/// back, the value expression, and the whole binder expression.
/// </summary>
/// <remarks>
/// <paramref name="Binder"/> holds the complete <c>CreateBinder(…)</c> call rather than just the
/// setter, so that the three setter shapes (inverted, synchronous, asynchronous) are resolved in the
/// analyzer and the emitter stays a single unconditional line. It is an
/// <see cref="ExpressionTemplate"/> and not a string because the analyzer composes it around the
/// author's own transplanted syntax, which may still contain unbound parameter holes from a
/// <c>[Composable]</c> expansion.
/// <para>
/// An element carries any number of these. <c>SetUpdatesAttributeName</c> writes to the immediately
/// preceding attribute frame, and the emitter calls it right after the event frame, so the frame it
/// writes is that binding's own event frame — which is exactly the frame <c>RenderTreeUpdater</c> reads
/// the name back from. Write and read are therefore per binding, and two bindings on one element each
/// keep their own resynchronized name (measured, #162). BCF3021 once rejected the second on the
/// grounds that they would collide; 付録B.5 records why that was withdrawn.
/// </para>
/// </remarks>
internal sealed record BindTemplate(
    string AttributeName,
    string EventName,
    ExpressionTemplate Value,
    ExpressionTemplate Binder);

internal sealed record ElementTemplateNode(
    string Tag,
    EquatableArray<ExpressionTemplate> Classes = default,       // folded class channel (RM1)
    EquatableArray<AttributeTemplate> Attributes = default,     // one frame each (RM2)
    EquatableArray<EventTemplate> Events = default,             // one frame each
    EquatableArray<RenderTemplateNode> Children = default) : RenderTemplateNode
{
    /// <summary>The element's two-way bindings in source order. Two frames each: the attribute, then the event.</summary>
    public EquatableArray<BindTemplate> Bindings { get; init; }
}

internal sealed record TextContentTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

/// <summary>Wrapper-less grouping: children emitted in sequence with no enclosing element frame.</summary>
internal sealed record FragmentTemplateNode(
    EquatableArray<RenderTemplateNode> Children = default) : RenderTemplateNode;

/// <summary>Trusted raw HTML injected verbatim via AddMarkupContent (MarkupString-equivalent).</summary>
internal sealed record RawMarkupTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

/// <summary>An externally supplied RenderFragment placed as content via AddContent (no wrapping element).</summary>
internal sealed record RenderFragmentContentTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

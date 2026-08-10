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

/// <summary>
/// One argument of a <c>[Composable]</c> call, in the callee's parameter order, carrying the source order
/// that expansion evaluates the arguments in.
/// </summary>
/// <remarks>
/// The parameter's type name is deliberately absent: expansion declares each local from the
/// <em>definition</em>'s <see cref="Analysis.ComposableParameter.TypeName"/>, so a per-argument copy was
/// written at every call site and read nowhere. The only place the callee's type name is still needed is
/// an omitted optional, where <c>ConstantTemplate.ForParameterDefault</c> spells the default's cast, and
/// that is computed in the branch that uses it.
/// </remarks>
internal sealed record ComposableInvocationArgument(
    int ParameterOrdinal,
    int SourceOrder,
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
/// How a binding writes its value back, which is the one thing about a <c>.Bind</c> that only the
/// analyzer can establish: the surface declares one overload per <c>(value type, setter shape)</c> pair,
/// so the shape is read off the overload the C# compiler picked and never guessed from the syntax.
/// </summary>
internal enum BindSetterKind
{
    /// <summary>
    /// No setter was written. The binder assigns back through the getter's own expression, which is why
    /// only this form requires an assignable target (BCF3018).
    /// </summary>
    InvertedGetter,

    /// <summary>A <see langword="void"/>-returning setter, passed as an <c>Action&lt;TValue&gt;</c>.</summary>
    Synchronous,

    /// <summary>
    /// A setter returning something other than <see langword="void"/>, passed through
    /// <c>RuntimeHelpers.CreateInferredBindSetter</c>.
    /// </summary>
    Asynchronous,
}

/// <summary>
/// A two-way binding on an element: the attribute carrying the current value, the event writing it
/// back, the value expression, and the facts the binder call is assembled from.
/// </summary>
/// <remarks>
/// The <c>CreateBinder(…)</c> call itself is not here. It is written by <c>RenderViewEmitter</c>, beside
/// the event channel's <c>EventCallback.Factory.Create</c> — which is the same call for the same job —
/// and under the CS8601/CS8620 suppression that only exists because of its shape. This record carries
/// what only the analyzer can supply: the resolved value type's name, which setter shape the overload
/// resolution picked, and the author's own setter syntax if there was one. Holding the assembled call
/// instead put the text in one layer and the suppression for it in another, with nothing connecting them
/// (#195).
/// <para>
/// <paramref name="Setter"/> is present exactly when <paramref name="SetterKind"/> is not
/// <see cref="BindSetterKind.InvertedGetter"/>. It is an <see cref="ExpressionTemplate"/> and not a
/// string for the reason <paramref name="Value"/> is: inside a <c>[Composable]</c> body either may still
/// hold unbound parameter holes, which <c>ComposableExpander</c> substitutes before the emitter reads
/// the code out.
/// </para>
/// <para>
/// An element carries any number of these. <c>SetUpdatesAttributeName</c> writes to the immediately
/// preceding attribute frame, and the emitter calls it right after the event frame, so the frame it
/// writes is that binding's own event frame — which is exactly the frame <c>RenderTreeUpdater</c> reads
/// the name back from. Write and read are therefore per binding, and two bindings on one element each
/// keep their own resynchronized name (measured, #162). BCF3021 once rejected the second on the
/// grounds that they would collide; 付録B.5 records why that was withdrawn.
/// </para>
/// </remarks>
/// <param name="ValueTypeName">
/// The bound value's type, fully qualified with no special-type spellings. Read by the one setter shape
/// whose binder needs a cast to name it.
/// </param>
internal sealed record BindTemplate(
    string AttributeName,
    string EventName,
    ExpressionTemplate Value,
    string ValueTypeName,
    BindSetterKind SetterKind,
    ExpressionTemplate? Setter);

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

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
/// <see cref="ComponentSlot"/> is separate from <see cref="ComponentParameter"/>: the content is a node
/// tree, and it takes part in hole substitution, sequence allocation, and expansion, none of which are
/// defined over <see cref="ExpressionTemplate"/>. It also carries no source order, because there is no
/// local to bind and therefore no evaluation to order — the subtree is spliced where the callee names it.
/// </remarks>
internal sealed record ViewPartContentArgument(int ParameterOrdinal, RenderTemplateNode Content);

internal abstract record RenderTemplateNode;

/// <summary>
/// A hole where a <c>[ViewPart]</c> body names content its caller supplies: <c>Html.Slot</c>, or a
/// reference to one of the definition's own <c>View</c>-typed parameters. Expansion replaces it with the
/// argument's own subtree, so no <see cref="RenderNode"/> counterpart exists and none reaches the emitter.
/// </summary>
/// <remarks>
/// One node type for both spellings because they are the same thing at different ordinals: the bracket
/// content is bound at <see cref="Analysis.ViewPartDefinition.SlotOrdinal"/> and a <c>View</c> parameter
/// at its own, and both are indices into the one substitution the expander carries.
/// </remarks>
internal sealed record ContentHoleTemplateNode(int ParameterOrdinal) : RenderTemplateNode;

internal sealed record IfTemplateNode(
    ExpressionTemplate Condition,
    RenderTemplateNode Then,
    RenderTemplateNode? Otherwise) : RenderTemplateNode;

internal sealed record ViewPartCallTemplateNode(
    string MethodKey,
    string DisplayName,
    EquatableArray<ViewPartInvocationArgument> Arguments,
    TemplateLocation Location) : RenderTemplateNode
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

/// <param name="Key">
/// The key selector's transplanted body, or <see langword="null"/> when the author declined it by writing
/// <c>null</c> (#172). A declined key emits no <c>SetKey</c>, is not asked about by BCF3002, and lifts
/// BCF3003, which exists only because <c>SetKey</c> needs an element or component frame to attach to.
/// </param>
internal sealed record ForEachTemplateNode(
    ExpressionTemplate Source,
    ExpressionTemplate? Key,
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
/// it takes part in hole substitution, sequence allocation, and [ViewPart] expansion, none of which are
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
/// back, the value expression, and the facts the binder call is assembled from.
/// </summary>
/// <remarks>
/// The <c>CreateBinder(…)</c> call itself is not here. It is written by <c>RenderViewEmitter</c>, beside
/// the event channel's <c>EventCallback.Factory.Create</c> — which is the same call for the same job —
/// and under the CS8601/CS8620 suppression that only exists because of its shape. This record carries
/// what only the analyzer can supply: the resolved value type's name, the setter shape overload
/// resolution picked, and the author's own setter syntax if there was one. Holding the assembled call
/// instead put the text in one layer and the suppression for it in another, with nothing connecting them
/// (#195).
/// <para>
/// The three setter shapes are spelled as a nullable setter plus one flag, matching
/// <see cref="Analysis.BindParameters"/>, which reads the same three off the same overload one layer up.
/// The alternative — a three-valued kind beside the nullable setter — would restate
/// <c>Setter is null</c> as an enum member and let the two disagree.
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
/// <param name="Setter">
/// The author's own setter, or <see langword="null"/> for the getter-only form, where the binder assigns
/// back through the getter's own expression — which is why only that form requires an assignable target
/// (BCF3018). An <see cref="ExpressionTemplate"/> and not a string for the reason
/// <paramref name="Value"/> is: inside a <c>[ViewPart]</c> body either may still hold unbound parameter
/// holes, which <c>ViewPartExpander</c> substitutes before the emitter reads the code out.
/// </param>
/// <param name="SetterIsAsynchronous">
/// Whether <paramref name="Setter"/> returns something other than <see langword="void"/>, and so is
/// wrapped by <c>RuntimeHelpers.CreateInferredBindSetter</c> rather than cast to an
/// <c>Action&lt;TValue&gt;</c>. Always <see langword="false"/> when there is no setter, so that one
/// binding has one spelling for the incremental cache to compare.
/// </param>
/// <param name="Culture">
/// The culture expression, or <see langword="null"/> for the <see langword="string"/> and
/// <see langword="bool"/> overloads that take none. Non-null is the one condition under which the
/// attribute frame's value is wrapped in <c>BindConverter.FormatValue</c>: the wrapping follows the
/// resolved overload and never the bound type, which is what keeps the pre-#307 output byte-identical.
/// An <see cref="ExpressionTemplate"/> for the reason <paramref name="Value"/> is — inside a
/// <c>[ViewPart]</c> body it may still hold unbound parameter holes.
/// </param>
/// <param name="Format">
/// The format expression, or <see langword="null"/>. Only ever non-null when <paramref name="Culture"/>
/// is, and only for a type BCF3031 admits.
/// </param>
internal sealed record BindTemplate(
    string AttributeName,
    string EventName,
    ExpressionTemplate Value,
    string ValueTypeName,
    ExpressionTemplate? Setter,
    bool SetterIsAsynchronous,
    ExpressionTemplate? Culture = null,
    ExpressionTemplate? Format = null);

internal sealed record ElementTemplateNode(
    string Tag,
    EquatableArray<ExpressionTemplate> Classes = default,       // folded class channel (RM1)
    EquatableArray<AttributeTemplate> Attributes = default,     // one frame each (RM2)
    EquatableArray<EventTemplate> Events = default,             // one frame each
    EquatableArray<RenderTemplateNode> Children = default) : RenderTemplateNode
{
    /// <summary>The element's two-way bindings in source order. Two frames each: the attribute, then the event.</summary>
    public EquatableArray<BindTemplate> Bindings { get; init; }

    /// <summary>
    /// The key written with <c>.Key</c>, or <see langword="null"/> when none was written or the author
    /// declined it with a literal <c>null</c> (§2.7(E)).
    /// </summary>
    /// <remarks>
    /// A channel of its own and not an <see cref="AttributeTemplate"/>: it lowers to <c>SetKey</c>, which
    /// consumes no sequence number and appends no frame. It is also what stops the element folding, since
    /// markup cannot carry a key. One key at most; a second is BCF3033.
    /// </remarks>
    public ExpressionTemplate? Key { get; init; }
}

internal sealed record TextContentTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

/// <summary>Wrapper-less grouping: children emitted in sequence with no enclosing element frame.</summary>
internal sealed record FragmentTemplateNode(
    EquatableArray<RenderTemplateNode> Children = default) : RenderTemplateNode;

/// <summary>Trusted raw HTML injected verbatim via AddMarkupContent (MarkupString-equivalent).</summary>
internal sealed record RawMarkupTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

/// <summary>An externally supplied RenderFragment placed as content via AddContent (no wrapping element).</summary>
internal sealed record RenderFragmentContentTemplateNode(ExpressionTemplate Content) : RenderTemplateNode;

/// <summary>
/// A call the generator cannot expand statically, rendered at runtime through the <c>RenderFragment</c>
/// the returned <c>View</c> carries (ARCHITECTURE.md §2.3 Opaque, §3.2).
/// </summary>
/// <remarks>
/// Opens no keyable frame, so <see cref="Analysis.KeyabilityResolver"/> answers
/// <c>ContentRootKind.Region</c> for it and BCF3003 rejects it as a <c>ForEach</c> content root — the same
/// answer <see cref="RenderFragmentContentTemplateNode"/> gets, and for the same reason.
/// </remarks>
internal sealed record OpaqueViewTemplateNode(ExpressionTemplate Call) : RenderTemplateNode;

/// <summary>
/// One generated scope: the names expansion mints for it (<paramref name="LocalCount"/>), and the
/// statements transplanted ahead of the content they lead into (ARCHITECTURE.md §2.3 Transplantable).
/// Either half may be absent, and a body that declares only through its expression has no statements at
/// all. The statements consume no sequence numbers, so the wrapped content keeps the width it has on its
/// own.
/// </summary>
/// <remarks>
/// The scope they land in depends on the position: a <c>ForEach</c> content block is written inside the
/// generated loop, which the region already isolates, while a design-time expression getter's block is
/// written into the body of <c>RenderView</c> itself, beside its builder parameter. That second position
/// is why the names the generated scope binds are refused rather than renamed
/// (<c>RenderExpressionAnalyzer.TryReadTransplantableBlock</c>). A <c>[ViewPart]</c> body's block lands
/// wherever the call was written, once per call, which is what <paramref name="LocalCount"/> answers.
/// <para>
/// Structurally the same shape as <c>ExpansionNode</c> — bindings, then a body that still owns the key —
/// and kept separate because the bindings differ: an expansion declares typed locals the expander built,
/// this carries statements the author wrote.
/// </para>
/// </remarks>
/// <param name="LocalCount">
/// How many locals this body declares as render variables, which is how many names expansion mints for it
/// (#336). The statements are not the only source: a designation written in the returned expression binds
/// into the same scope, so a body with no statements at all reaches this node when it declares one (#343).
/// Zero for a component's own expression, whose authored names stand as written. A count and not the
/// names: the holes carry the ordinals, so the names exist only at expansion.
/// </param>
internal sealed record TransplantedBlockTemplateNode(
    ExpressionTemplate Statements, RenderTemplateNode Content, int LocalCount) : RenderTemplateNode;

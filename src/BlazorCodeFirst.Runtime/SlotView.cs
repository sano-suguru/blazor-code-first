namespace BlazorCodeFirst;

/// <summary>
/// Inert design-time syntax for a <c>[ViewPart]</c> part that takes caller-supplied content: the result
/// of calling one, before the content has been supplied. The indexer supplies it and completes the call.
/// The name states an invariant that holds in both directions: a part declares this return type exactly
/// when it has slots, and a part with this return type names <see cref="Html.Slot"/> exactly once
/// (BCF3025). Both slot channels are covered, the bracket hole and the additional named slots, which are
/// the <see cref="View"/> parameters a part returning <see cref="View"/> may not have (BCF1002).
/// </summary>
/// <remarks>
/// <para>
/// This is the <c>[ViewPart]</c> counterpart of <see cref="ComponentView{TComponent}"/>. A part that
/// takes content declares <see cref="SlotView"/> as its return type and writes <see cref="Html.Slot"/>
/// in its body where the content belongs, so the call reads the way an element reads —
/// <c>Card("Profile")[P["本文"]]</c> beside <c>Div.Class("card")[P["本文"]]</c>. `DESIGN.md` §4.3 records
/// why that spelling was chosen over a positional <c>View</c> argument (#176).
/// </para>
/// <para>
/// There is deliberately no conversion to <see cref="View"/>, and that single omission is what makes the
/// surface's rules enforceable by C# rather than by diagnostics. A call whose brackets were forgotten,
/// <c>Div[Card("x")]</c>, is not a <see cref="View"/> and so is not a child; and it cannot be a
/// <c>Body</c> either. The conversion in the other direction exists because a part's expression body
/// produces a <see cref="View"/> and has to reach this declared return type; being one-way, it cannot
/// carry a <see cref="SlotView"/> anywhere a <see cref="View"/> is wanted.
/// </para>
/// <para>
/// It cannot be decorated, for the reason <c>Fragment</c> and <c>Raw</c> cannot (`DESIGN.md` §4.1): no
/// single element frame is opened, so a decoration would have no target. That needs no diagnostic either
/// — every decoration is an extension method on <see cref="ElementView"/>, so
/// <c>Card("t").Class("x")</c> is a CS1929, the same way <c>Div["x"].Class("y")</c> already is.
/// </para>
/// <para>
/// Like every other member of the design-time surface it is never meant to run: at runtime the indexer
/// and the conversion do no work and return the default value.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1043:Use integral or string argument for indexers",
    Justification = "The indexer is the content channel of a [ViewPart] part, not a lookup by index: its " +
        "argument is the content the caller is wrapping, a mixed sequence of strings and Views that no " +
        "integral or string overload could carry. It is the same channel ElementView and " +
        "ComponentView<T> declare, for the same reason and with the same signature.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "SlotView is an inert design-time marker carrying no state; it is read by the " +
        "source generator and is always the default value at runtime, so equality is structurally " +
        "determined and needs no override. Same rationale as View and ElementView.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The conversion is inert design-time syntax read by the source generator: it exists so " +
        "a [ViewPart] body, which is a View expression, converts to this declared return type, and it " +
        "always yields the default SlotView at runtime. A named alternate would imply it does work.")]
public readonly struct SlotView
{
    /// <summary>
    /// Design-time syntax supplying the content this part wraps. Inert: the generator reads the original
    /// child expressions and splices them into the part's <see cref="Html.Slot"/> position; at runtime this
    /// always yields the default View.
    /// </summary>
    /// <param name="children">Mixed string and <see cref="View"/> content, in source order.</param>
    /// <returns>The marker <see cref="View"/>; never evaluated at runtime.</returns>
    public View this[params System.ReadOnlySpan<View> children] => default;

    /// <summary>
    /// Design-time syntax letting a <c>[ViewPart]</c> body, which is a <see cref="View"/> expression,
    /// satisfy a <see cref="SlotView"/> return type. Inert: the generator reads the original expression;
    /// at runtime this always yields the default SlotView.
    /// </summary>
    public static implicit operator SlotView(View view) => default;
}

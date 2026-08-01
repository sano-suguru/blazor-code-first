namespace BlazorCompose;

/// <summary>
/// Inert design-time syntax for an HTML element that has been named but not yet given children. Every
/// <see cref="Html"/> element factory produces one; decorations (<see cref="Decorations.Class"/> and the
/// rest) take and return one; the indexer supplies the children and completes the element.
/// </summary>
/// <remarks>
/// <para>
/// Attributes bind to the builder and children bind to the indexer, so an element is written the way HTML
/// writes it — <c>Div.Class("card")["text"]</c>, attributes next to the tag. The indexer returns
/// <see cref="View"/> rather than another builder, which is what makes the reverse spelling
/// (<c>Div["text"].Class("card")</c>) a CS1929 rather than a second supported style.
/// </para>
/// <para>
/// This is a <c>readonly struct</c> and deliberately not a <c>ref struct</c>: a <c>ref struct</c> cannot be
/// a generic type argument (CS0306), so <c>.Param(c =&gt; c.Payload, Div)</c> would fail to compile and
/// BC3014 — which exists to reject exactly that expression with a purpose-written message — would never be
/// reached. The <c>[Composable]</c> parameter rejection depends on the same property.
/// </para>
/// <para>
/// Like every other member of the design-time surface it is never meant to run: at runtime the indexer and
/// the conversion do no work and return the default value.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1043:Use integral or string argument for indexers",
    Justification = "The indexer is the children channel of an element, not a lookup by index: its argument " +
        "is the element's content, which is a mixed sequence of strings and Views and cannot be expressed " +
        "as an integer or a string. The bracket spelling is the point — it places attributes next to the " +
        "tag, as HTML does — and no integral or string overload could carry it.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "ElementBuilder is an inert design-time marker carrying no state; it is read by the " +
        "source generator and is always the default value at runtime, so equality is structurally " +
        "determined and needs no override. Same rationale as View.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The conversion mirrors the Html factories: it is inert design-time syntax read by the " +
        "source generator so a childless element can appear wherever a View is expected, and it always " +
        "yields the default View at runtime. A named alternate would imply it does work.")]
public readonly struct ElementBuilder
{
    /// <summary>
    /// Design-time syntax supplying the element's children. Inert: the generator reads the original child
    /// expressions and emits the enclosing element's frames; at runtime this always yields the default View.
    /// </summary>
    /// <param name="children">Mixed string and <see cref="View"/> content, in source order.</param>
    /// <returns>The marker <see cref="View"/>; never evaluated at runtime.</returns>
    public View this[params System.ReadOnlySpan<View> children] => default;

    /// <summary>
    /// Design-time syntax letting a childless element compose as content. Inert: the generator reads the
    /// original expression; at runtime this always yields the default View.
    /// </summary>
    public static implicit operator View(ElementBuilder builder) => default;
}

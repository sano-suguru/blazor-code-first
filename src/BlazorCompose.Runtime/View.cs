namespace BlazorCompose;

/// <summary>
/// A design-time-only marker for a node in a <see cref="ComposeComponentBase.Body"/> expression.
/// </summary>
/// <remarks>
/// <see cref="View"/> is inert syntax analyzed by the BlazorCompose source generator, not a runtime UI
/// value. It carries no state and is never rendered directly; the generator translates the
/// <c>Body</c> expression that produces it into a <c>RenderBody</c> override. Instances observed at
/// runtime are always the default value and must not be inspected or acted upon.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "The string conversion mirrors the UI factories: it is inert design-time syntax " +
        "read by the source generator (a string argument in element content becomes a text node) and " +
        "always yields the default View at runtime, so a named alternate would misleadingly imply work.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1815:Override equals and operator equals on value types",
    Justification = "View is an inert marker type carrying no state; it is always the default value. " +
        "Equality is structurally determined and needs no override.")]
public readonly struct View
{
    /// <summary>
    /// Design-time syntax letting a raw string appear as element content (a text node). Inert: the
    /// generator reads the original string expression; at runtime this always yields the default View.
    /// </summary>
    public static implicit operator View(string text) => default;
}

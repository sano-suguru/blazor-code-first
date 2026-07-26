namespace BlazorCompose;

/// <summary>
/// A design-time-only marker for a node in a Compose design-time expression
/// (<see cref="ComposeComponentBase.Body"/> or <see cref="ComposeLayoutBase.Chrome"/>).
/// </summary>
/// <remarks>
/// <see cref="View"/> is inert syntax analyzed by the BlazorCompose source generator, not a runtime UI
/// value. It carries no state and is never rendered directly; the generator translates the design-time
/// expression (<c>Body</c> or <c>Chrome</c>) that produces it into a <c>RenderView</c> override.
/// Instances observed at runtime are always the default value and must not be inspected or acted upon.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "Both conversions (string, RenderFragment?) mirror the Html factories: they are inert " +
        "design-time syntax read by the source generator (a string argument in element content becomes a " +
        "text node; a RenderFragment argument becomes an AddContent call) and always yield the default " +
        "View at runtime, so a named alternate would misleadingly imply work.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "View is an inert marker type carrying no state on the SSC path; it is always the " +
        "default value there. Equality is structurally determined and needs no override. ARCHITECTURE.md " +
        "§3.2 plans an internal RenderFragment field for the Opaque path; revisit this suppression then.")]
public readonly struct View
{
    /// <summary>
    /// Design-time syntax letting a raw string appear as element content (a text node). Inert: the
    /// generator reads the original string expression; at runtime this always yields the default View.
    /// </summary>
    public static implicit operator View(string text) => default;

    /// <summary>
    /// Design-time syntax letting an externally supplied
    /// <see cref="Microsoft.AspNetCore.Components.RenderFragment"/> appear as element content. Inert:
    /// the generator reads the original expression and emits
    /// <c>RenderTreeBuilder.AddContent(sequence, fragment)</c>; at runtime this always yields the
    /// default View.
    /// </summary>
    /// <remarks>
    /// The parameter is nullable because null is the normal case — an unset
    /// <c>[Parameter] RenderFragment?</c>, or a layout's Body before the first render. Blazor's
    /// <c>AddContent(int, RenderFragment?)</c> emits nothing for null.
    /// <para>
    /// This conversion is inert only for the SSC path. ARCHITECTURE.md §3.2 specifies that the Opaque
    /// path gives <see cref="View"/> an internal <c>RenderFragment</c> field; when that path (or the
    /// DEBUG interpretation mode of ARCHITECTURE.md appendix C) is implemented, this operator gains a
    /// real body. <c>=&gt; default</c> is the current SSC-only behavior, not a permanent contract.
    /// </para>
    /// </remarks>
    public static implicit operator View(Microsoft.AspNetCore.Components.RenderFragment? fragment) => default;
}

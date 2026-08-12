namespace BlazorCodeFirst;

/// <summary>
/// A design-time-only marker for a node in a BlazorCodeFirst design-time expression
/// (<see cref="BodyComponentBase.Body"/> or <see cref="ChromeLayoutBase.Chrome"/>).
/// </summary>
/// <remarks>
/// <see cref="View"/> is inert syntax analyzed by the BlazorCodeFirst source generator, not a runtime UI
/// value. It carries no state and is never rendered directly; the generator translates the design-time
/// expression (<c>Body</c> or <c>Chrome</c>) that produces it into a <c>RenderView</c> override.
/// On the SSC path an instance observed at runtime is always the default value. On the Opaque path
/// (ARCHITECTURE.md §2.3, §3.2) it carries the fragment the generator renders through
/// <see cref="CompilerServices.ViewRuntime.FragmentOf(View)"/>.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Usage",
    "CA2225:Operator overloads have named alternates",
    Justification = "Both conversions (string, RenderFragment?) mirror the Html element helpers: they are inert " +
        "design-time syntax read by the source generator (a string argument in element content becomes a " +
        "text node; a RenderFragment argument becomes an AddContent call), so a named alternate would " +
        "misleadingly imply work.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1815:Override equals and operator equals on value types",
    Justification = "View carries a fragment reference on the Opaque path and nothing at all on the SSC " +
        "path. The default struct comparison already compares that one field, and no caller compares two " +
        "Views, so an override would add a member nothing reads.")]
public readonly struct View
{
    /// <summary>
    /// The fragment this <see cref="View"/> renders on the Opaque path (ARCHITECTURE.md §3.2), or
    /// <see langword="null"/> on the SSC path, where the generator reads the syntax and the value is never
    /// observed. Read through <see cref="CompilerServices.ViewRuntime.FragmentOf(View)"/>, which generated
    /// code in the consumer's own assembly calls because this field is internal to this one.
    /// </summary>
    internal readonly Microsoft.AspNetCore.Components.RenderFragment? Fragment;

    internal View(Microsoft.AspNetCore.Components.RenderFragment? fragment) => Fragment = fragment;

    /// <summary>
    /// Design-time syntax letting a raw string appear as element content (a text node). Inert: the
    /// generator reads the original string expression; at runtime this always yields the default View.
    /// </summary>
    public static implicit operator View(string text) => default;

    /// <summary>
    /// Design-time syntax letting an externally supplied
    /// <see cref="Microsoft.AspNetCore.Components.RenderFragment"/> appear as element content. The
    /// generator reads the original expression and emits
    /// <c>RenderTreeBuilder.AddContent(sequence, fragment)</c>.
    /// </summary>
    /// <remarks>
    /// The parameter is nullable because null is the normal case, an unset
    /// <c>[Parameter] RenderFragment?</c>, or a layout's Body before the first render. Blazor's
    /// <c>AddContent(int, RenderFragment?)</c> emits nothing for null.
    /// <para>
    /// This is the only route into <see cref="Fragment"/>. Every member of <see cref="Html"/>,
    /// <see cref="ElementView"/> and <see cref="Decorations"/> returns the default value, so a
    /// <see cref="View"/> built from the design-time surface carries no fragment and renders nothing if it
    /// reaches the Opaque path — which is what BCF3030 exists to stop.
    /// </para>
    /// </remarks>
    public static implicit operator View(Microsoft.AspNetCore.Components.RenderFragment? fragment) =>
        new(fragment);
}

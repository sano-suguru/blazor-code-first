using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// The argument roles of a resolved event decoration — which argument is the handler, and which carries the
/// event's name when the decoration does not name it itself — answered once by
/// <see cref="KnownSymbols.TryGetEventParameters"/> and read by everything that has to know which argument
/// of an event decoration is which.
/// </summary>
/// <remarks>
/// <para>
/// The event side of what <see cref="BindParameters"/> settled for <c>.Bind</c> (#206). Three places
/// answered "which argument is the handler" and none of them asked the other two: <c>BCF3001</c>'s
/// deferred-handler exemption by the bound parameter's type, the decoration arm of
/// <see cref="RenderExpressionAnalyzer"/> by position, and <c>KnownSymbolsSyncTests</c> by the shape
/// <c>(ElementBuilder, delegate)</c>. They agreed only because every event decoration the runtime declares
/// happens to carry one delegate parameter in the position the others assumed, and nothing held them there
/// (#221).
/// </para>
/// <para>
/// Indices are in <em>argument space</em> — an extension method's receiver excluded — which is the space
/// <see cref="FactoryArguments.At"/> indexes in, and no <see cref="IParameterSymbol"/> is exposed. Both for
/// the reasons <see cref="BindParameters"/>' own remarks give.
/// </para>
/// </remarks>
internal readonly struct EventParameters
{
    /// <summary>The handler's argument index.</summary>
    public int HandlerIndex { get; }

    /// <summary>
    /// The argument index of the event's name, or <c>-1</c> when the decoration names the event itself. A
    /// named shortcut is the second case: <c>.OnClick(handler)</c> stands for <c>onclick</c>, which
    /// <see cref="KnownSymbols.EventShortcuts"/> supplies and no argument carries.
    /// </summary>
    public int EventNameIndex { get; }

    internal EventParameters(int handlerIndex, int eventNameIndex)
    {
        HandlerIndex = handlerIndex;
        EventNameIndex = eventNameIndex;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is this decoration's handler parameter, whichever spelling it
    /// was resolved from.
    /// </summary>
    /// <remarks>
    /// By normalized position, never by symbol identity and never by delegate shape. Identity fails across
    /// spellings for the reason <see cref="BindParameters.IsSetter"/>'s remarks record. The delegate shape
    /// is what this replaces, and its defect is that it cannot tell a handler from a second delegate
    /// argument: every one of them satisfies it.
    /// </remarks>
    public bool IsHandler(IParameterSymbol? candidate) =>
        candidate is not null && KnownSymbols.ArgumentIndex(candidate) == HandlerIndex;
}

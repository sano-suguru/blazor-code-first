using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// The parameter roles of a resolved <c>Bind</c> overload — the getter's argument index and therefore
/// the setter's — answered once by <see cref="KnownSymbols.TryGetBindParameters"/> and read by
/// everything that has to know which argument of a binding is which.
/// </summary>
/// <remarks>
/// <para>
/// One struct rather than four separate derivations, because the surface declares one overload per
/// <c>(value type, setter shape)</c> pair and a new pair moves every ordinal at once. The emitter arms,
/// the failure-path scanner and BCF3001's exemption each used to encode "the setter follows the getter"
/// in a convention of their own, and they would have gone out of step independently and in different
/// directions (#206).
/// </para>
/// <para>
/// Indices are in <em>argument space</em> — an extension method's receiver excluded — which is the space
/// <see cref="FactoryArguments.At"/> indexes in. No <see cref="IParameterSymbol"/> is exposed: Roslyn
/// hands the same call's symbol out in different spellings (see
/// <see cref="KnownSymbols.TryGetBindParameters"/>), so a parameter that escaped this struct would
/// invite exactly the cross-spelling comparison it exists to make unnecessary.
/// </para>
/// </remarks>
internal readonly struct BindParameters
{
    /// <summary>The getter's argument index.</summary>
    public int GetterIndex { get; }

    /// <summary>
    /// The setter's argument index, or <c>-1</c> when the overload declares no setter. The argument
    /// readers on both paths (<see cref="FactoryArguments.At"/> and the failure scanner's
    /// <c>BoundArguments.At</c>) gate on an unsigned range comparison, so both answer
    /// <see langword="null"/> for <c>-1</c> and a call site reading the setter argument needs no
    /// branch of its own. This encoding holds only when
    /// <see cref="KnownSymbols.TryGetBindParameters"/> answered <see langword="true"/>; the
    /// <see langword="default"/> value of this struct (as left behind on a <see langword="false"/>
    /// answer) carries no meaning and must not be read.
    /// </summary>
    public int SetterIndex { get; }

    /// <summary>The bound value's type: the getter delegate's return type.</summary>
    public ITypeSymbol ValueType { get; }

    /// <summary>
    /// Whether the setter returns something other than <see langword="void"/>. Meaningless when
    /// <see cref="SetterIndex"/> is <c>-1</c>.
    /// </summary>
    public bool SetterIsAsynchronous { get; }

    internal BindParameters(int getterIndex, int setterIndex, ITypeSymbol valueType, bool setterIsAsynchronous)
    {
        GetterIndex = getterIndex;
        SetterIndex = setterIndex;
        ValueType = valueType;
        SetterIsAsynchronous = setterIsAsynchronous;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is this overload's setter parameter, whichever spelling it
    /// was resolved from.
    /// </summary>
    /// <remarks>
    /// By normalized position, never by symbol identity. The two are not interchangeable: a caller
    /// holding a method from <c>GetSymbolInfo</c> and a parameter from an operation's
    /// <c>IArgumentOperation.Parameter</c> holds symbols whose containing methods differ in reduction
    /// and, for a generic <c>Bind</c>, in construction. An identity comparison would answer
    /// <see langword="false"/> for the setter it was handed.
    /// </remarks>
    public bool IsSetter(IParameterSymbol? candidate) =>
        SetterIndex >= 0 && candidate is not null && ArgumentIndex(candidate) == SetterIndex;

    /// <summary>
    /// <paramref name="parameter"/>'s index in argument space, the receiver excluded.
    /// </summary>
    /// <remarks>
    /// A classic <c>this</c> extension method carries its receiver at ordinal 0 only in its unreduced
    /// spelling. The reduced spelling, an instance method, and a C# 14 extension member all exclude it
    /// already — the last because Roslyn answers <see cref="IMethodSymbol.IsExtensionMethod"/> with
    /// <see langword="false"/> for that declaration form and hangs the receiver off the containing
    /// extension block instead (#203).
    /// </remarks>
    internal static int ArgumentIndex(IParameterSymbol parameter) =>
        parameter.ContainingSymbol is IMethodSymbol { IsExtensionMethod: true, ReducedFrom: null }
            ? parameter.Ordinal - 1
            : parameter.Ordinal;
}

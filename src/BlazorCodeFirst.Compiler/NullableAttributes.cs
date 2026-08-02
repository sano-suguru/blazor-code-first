// Polyfill required for nullable-analysis attributes targeting netstandard2.0.
// System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute is not available in the netstandard2.0
// reference assemblies; it can be provided inline for the compiler to honor on public APIs.
namespace System.Diagnostics.CodeAnalysis;

/// <summary>Specifies that when a method returns <see cref="ReturnValue"/>, the parameter may be <see langword="null"/>.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class MaybeNullWhenAttribute : Attribute
{
    /// <summary>Initializes the attribute with the specified return value condition.</summary>
    /// <param name="returnValue">
    /// The return value condition. When the method returns this value, the associated parameter may be <see langword="null"/>.
    /// </param>
    public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

    /// <summary>Gets the return value condition.</summary>
    public bool ReturnValue { get; }
}

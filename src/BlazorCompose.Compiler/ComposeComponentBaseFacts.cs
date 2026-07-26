using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler;

/// <summary>Shared symbol predicates for the BlazorCompose base classes.</summary>
internal static class ComposeComponentBaseFacts
{
    /// <summary>
    /// True when <paramref name="type"/> is one of the BlazorCompose base classes. Detection is by name
    /// because there are exactly two and they are framework-owned; a structural probe would be slower
    /// and could match unrelated user types.
    /// </summary>
    private static bool IsComposeBase(INamedTypeSymbol type) =>
        type.Name is "ComposeComponentBase" or "ComposeLayoutBase" &&
        type.ContainingNamespace is { IsGlobalNamespace: false, Name: "BlazorCompose" } ns &&
        ns.ContainingNamespace.IsGlobalNamespace;

    /// <summary>
    /// Returns the Compose base found in <paramref name="symbol"/>'s base-type chain (<c>ComposeComponentBase</c>
    /// or <c>ComposeLayoutBase</c>), or <see langword="null"/> when none is found. The single walk backing
    /// both <see cref="InheritsFromComposeBase"/> and <see cref="FindDesignTimeExpressionName"/>, and the
    /// source of the base type's own name for diagnostic messages.
    /// </summary>
    internal static INamedTypeSymbol? FindComposeBase(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (IsComposeBase(current))
                return current;
        }

        return null;
    }

    internal static bool InheritsFromComposeBase(INamedTypeSymbol symbol) =>
        FindComposeBase(symbol) is not null;

    /// <summary>
    /// The name of the abstract <c>BlazorCompose.View</c> property the Compose base declares — <c>Body</c>
    /// on <c>ComposeComponentBase</c>, <c>Chrome</c> on <c>ComposeLayoutBase</c> — or <see langword="null"/>
    /// when <paramref name="symbol"/> is not a Compose component.
    /// </summary>
    /// <remarks>
    /// Resolved from the base symbol rather than hard-coded, so the compiler carries no property-name
    /// literal and cannot mistake an unrelated override that happens to share the name.
    /// </remarks>
    internal static string? FindDesignTimeExpressionName(INamedTypeSymbol symbol)
    {
        if (FindComposeBase(symbol) is not { } composeBase)
            return null;

        foreach (var member in composeBase.GetMembers())
        {
            if (member is IPropertySymbol { IsAbstract: true } property && IsViewType(property.Type))
                return property.Name;
        }

        return null;
    }

    private static bool IsViewType(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "View" } named &&
        named.ContainingNamespace is { IsGlobalNamespace: false, Name: "BlazorCompose" } ns &&
        ns.ContainingNamespace.IsGlobalNamespace;
}

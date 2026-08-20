using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler;

/// <summary>Shared symbol predicates for the BlazorCodeFirst base classes.</summary>
internal static class DesignTimeBaseFacts
{
    /// <summary>
    /// True when <paramref name="type"/> is one of the BlazorCodeFirst base classes. Detection is by name
    /// because there are exactly two and they are framework-owned; a structural probe would be slower
    /// and could match unrelated user types.
    /// </summary>
    private static bool IsDesignTimeBase(INamedTypeSymbol type) =>
        type.Name is "BodyComponentBase" or "ChromeLayoutBase" &&
        type.ContainingNamespace is { IsGlobalNamespace: false, Name: "BlazorCodeFirst" } ns &&
        ns.ContainingNamespace.IsGlobalNamespace;

    /// <summary>
    /// Returns the base class found in <paramref name="symbol"/>'s base-type chain (<c>BodyComponentBase</c>
    /// or <c>ChromeLayoutBase</c>), or <see langword="null"/> when none is found. The single walk backing
    /// <see cref="FindDesignTimeExpressionName"/>, and the source of the base type's own name for
    /// diagnostic messages.
    /// </summary>
    internal static INamedTypeSymbol? FindDesignTimeBase(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (IsDesignTimeBase(current))
                return current;
        }

        return null;
    }

    /// <summary>
    /// The name of the abstract <c>BlazorCodeFirst.View</c> property the base class declares, <c>Body</c>
    /// on <c>BodyComponentBase</c>, <c>Chrome</c> on <c>ChromeLayoutBase</c>, or <see langword="null"/>
    /// when <paramref name="symbol"/> is not a BlazorCodeFirst component.
    /// </summary>
    /// <remarks>
    /// Resolved from the base symbol rather than hard-coded, so the compiler carries no property-name
    /// literal and cannot mistake an unrelated override that happens to share the name.
    /// </remarks>
    internal static string? FindDesignTimeExpressionName(INamedTypeSymbol symbol)
    {
        if (FindDesignTimeBase(symbol) is not { } designTimeBase)
            return null;

        foreach (var member in designTimeBase.GetMembers())
        {
            if (member is IPropertySymbol { IsAbstract: true } property && IsViewType(property.Type))
                return property.Name;
        }

        return null;
    }

    private static bool IsViewType(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "View" } named &&
        named.ContainingNamespace is { IsGlobalNamespace: false, Name: "BlazorCodeFirst" } ns &&
        ns.ContainingNamespace.IsGlobalNamespace;
}

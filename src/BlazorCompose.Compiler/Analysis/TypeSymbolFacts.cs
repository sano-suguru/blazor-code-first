using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Analysis;

internal static class TypeSymbolFacts
{
    public static bool ContainsUnresolvedType(ITypeSymbol type)
    {
        switch (type)
        {
            case { TypeKind: TypeKind.Error }:
                return true;
            case ITypeParameterSymbol:
                return false;
            case IArrayTypeSymbol array:
                return ContainsUnresolvedType(array.ElementType);
            case IPointerTypeSymbol pointer:
                return ContainsUnresolvedType(pointer.PointedAtType);
            case INamedTypeSymbol named:
                for (var containing = named.ContainingType;
                     containing is not null;
                     containing = containing.ContainingType)
                {
                    if (ContainsUnresolvedType(containing))
                        return true;
                }

                foreach (var argument in named.TypeArguments)
                {
                    if (ContainsUnresolvedType(argument))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }
}

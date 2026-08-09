using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

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

    /// <summary>
    /// Whether <paramref name="type"/> can be written as a fully qualified type name in a generated file
    /// that carries no <c>using</c> directives and is not <c>unsafe</c>. Anonymous types, pointer types,
    /// open type parameters, file-local types, function pointers, and otherwise unnameable types cannot be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One predicate for one question, asked from both places a name has to survive the move into generated
    /// code: the type argument an extension method fixes
    /// (<see cref="ExpressionTemplateFactory"/>), and the declared type of a <c>[Composable]</c> parameter,
    /// which becomes a local at the expansion site (<see cref="ComposableDefinitionFactory"/>). Both failures
    /// reach the author as the same BCF1002, so answering them by two rules meant one error message covering
    /// two different definitions of "cannot be named".
    /// </para>
    /// <para>
    /// Every arm answers conservatively, because the cost is asymmetric: a rejection is a located diagnostic
    /// the author can act on, while an acceptance the generated file cannot honour is a compiler error inside
    /// generated code, pointing at a line nobody wrote. That is what settles the two arms the composable copy
    /// used to answer the other way. A pointer parameter is rejected rather than followed to its pointed-at
    /// type, because the generated file is not <c>unsafe</c> and could not declare the local whatever the
    /// pointed-at type turned out to be. An open type parameter is rejected rather than admitted; the
    /// composable route validates its methods as non-generic before reaching here, so that arm is unreachable
    /// from it and only the extension-method route depends on the answer.
    /// </para>
    /// <para>
    /// A function pointer changes answer for the same reason and by the same route, though nothing in the
    /// switch names it: <see cref="IFunctionPointerTypeSymbol"/> matches no case above and falls to
    /// <c>default</c>, where the composable copy's <c>default</c> used to mean "nameable". It is left to fall
    /// through rather than given an arm, because an arm would suggest the type needs handling when what it
    /// needs is the default answer. Like the pointer arm it is unreachable in practice — a
    /// <c>delegate*&lt;…&gt;</c> parameter requires an <c>unsafe</c> context — and untested for the same
    /// reason: covering it would mean turning on <c>AllowUnsafeBlocks</c> across
    /// <c>CompilationTestHost</c>, changing what every other test compiles under.
    /// </para>
    /// </remarks>
    public static bool IsNameableInGeneratedCode(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsNameableInGeneratedCode(array.ElementType);

            case IPointerTypeSymbol:
                return false;

            case ITypeParameterSymbol:
                return false;

            case IDynamicTypeSymbol:
                return true;

            case INamedTypeSymbol named:
                if (named.IsAnonymousType || named.IsFileLocal || !named.CanBeReferencedByName)
                    return false;

                for (var containing = named.ContainingType; containing is not null; containing = containing.ContainingType)
                {
                    if (containing.IsFileLocal || !containing.CanBeReferencedByName)
                        return false;
                }

                foreach (var argument in named.TypeArguments)
                {
                    if (!IsNameableInGeneratedCode(argument))
                        return false;
                }

                return true;

            default:
                return false;
        }
    }
}

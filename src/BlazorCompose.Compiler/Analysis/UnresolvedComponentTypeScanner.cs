using System.Linq;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Sweeps a design-time expression that failed to translate, reporting BC3012 for every
/// <c>Html.Component&lt;T&gt;()</c> call whose type argument does not resolve.
/// </summary>
/// <remarks>
/// A sweep rather than a check inside <see cref="RenderExpressionAnalyzer"/>'s Component branch,
/// because the analyzer often never reaches that branch. Any call whose arguments contain a lambda —
/// <c>If</c>, <c>ForEach</c>, or a decoration applied after <c>.Param</c> — has an outer
/// <c>GetSymbolInfo</c> that degrades to a null symbol when an unresolved type is nested inside it, and
/// the analyzer exits at its early symbol guard without recursing into the lambda body. Running on the
/// failure path instead makes the report independent of where translation gave up.
/// </remarks>
internal static class UnresolvedComponentTypeScanner
{
    /// <summary>
    /// Records BC3012 into <paramref name="context"/> for every unresolved <c>Component&lt;T&gt;()</c>
    /// type argument under <paramref name="root"/>, in syntactic order, once per invocation.
    /// </summary>
    public static void Report(ExpressionSyntax root, ComposableBodyContext context)
    {
        var componentMethod = context.KnownSymbols.HtmlComponent;
        if (componentMethod is null)
            return;

        foreach (var invocation in root.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // The inner Component<T>() call resolves cleanly even when the enclosing call does not, so
            // Symbol is normally present here; CandidateSymbols is the fallback for a Component call
            // that is itself the unresolvable one. Pattern-matched rather than `as` + null check: the
            // latter is IDE0019, an error in this repo.
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
            if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method ||
                !SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, componentMethod))
            {
                continue;
            }

            if (method.TypeArguments.Length != 1 || !ContainsUnresolvedType(method.TypeArguments[0]))
                continue;

            if (FindTypeArgumentSyntax(invocation) is not { } typeArgument)
                continue;

            // A collection expression, not ImmutableArray.Create(x) — the latter is IDE0303, an error here.
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BC3012,
                typeArgument.GetLocation(),
                [typeArgument.ToString()]));
        }
    }

    /// <summary>
    /// True when <paramref name="type"/> is, or structurally contains, a type that did not resolve.
    /// </summary>
    /// <remarks>
    /// The <c>ContainingType</c> walk is load-bearing, not defensive: for
    /// <c>Component&lt;Outer&lt;Missing&gt;.Inner&gt;()</c> the type argument is a resolved
    /// <c>TypeKind.Class</c> whose <c>TypeArguments</c> is empty, and the unresolved type is only
    /// reachable through <c>ContainingType</c>. <c>ComposableDefinitionFactory.IsUnnameableType</c>
    /// walks it for the same class of reason. A type parameter is never unresolved — a generic
    /// component's <c>Component&lt;TChild&gt;()</c> is supported and must not be reported.
    /// </remarks>
    public static bool ContainsUnresolvedType(ITypeSymbol type)
    {
        switch (type)
        {
            case { TypeKind: TypeKind.Error }:
                return true;

            case ITypeParameterSymbol:
                return false;

            // Unreachable today: Component<T>'s `where T : IComponent` constraint rejects an array, so
            // Component<Missing[]>() fails overload resolution and never matches HtmlComponent at all
            // (it lands on BC1003 — measured). Kept for structural completeness of the predicate.
            case IArrayTypeSymbol array:
                return ContainsUnresolvedType(array.ElementType);

            case INamedTypeSymbol named:
                for (var containing = named.ContainingType; containing is not null; containing = containing.ContainingType)
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
    /// The written type-argument syntax of a <c>Component&lt;T&gt;()</c> invocation: the generic name is
    /// the invocation's own expression for the unqualified spelling, or the <c>.Name</c> of a member
    /// access for <c>Html.Component&lt;T&gt;()</c>.  Returns <see langword="null"/> for any other shape.
    /// </summary>
    private static TypeSyntax? FindTypeArgumentSyntax(InvocationExpressionSyntax invocation)
    {
        var generic = invocation.Expression switch
        {
            GenericNameSyntax direct => direct,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax qualified } => qualified,
            _ => null,
        };

        // A list pattern on a SeparatedSyntaxList needs System.Index.GetOffset, unavailable on
        // netstandard2.0 (CS0656); check Count and index explicitly.
        return generic is not null && generic.TypeArgumentList.Arguments.Count == 1
            ? generic.TypeArgumentList.Arguments[0]
            : null;
    }
}

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
        var componentWithChildrenMethod = context.KnownSymbols.HtmlComponentWithChildren;
        if (componentMethod is null && componentWithChildrenMethod is null)
            return;

        foreach (var invocation in root.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // The inner Component<T>() call resolves cleanly even when the enclosing call does not, so
            // Symbol is present in every currently-known shape; CandidateSymbols is defensive with no
            // shape known to reach it. Pattern-matched rather than `as` + null check: the latter is
            // IDE0019, an error in this repo.
            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
            if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method
                || !IsComponentFactory(method, componentMethod, componentWithChildrenMethod))
            {
                continue;
            }

            if (method.TypeArguments.Length != 1
                || !TypeSymbolFacts.ContainsUnresolvedType(method.TypeArguments[0]))
            {
                continue;
            }

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
    /// Whether <paramref name="method"/> is either <c>Html.Component&lt;T&gt;()</c> overload. Both must be
    /// matched: the params form is a distinct symbol, and missing it would report BC1003 for an
    /// unresolved type argument instead of BC3012.
    /// </summary>
    private static bool IsComponentFactory(
        IMethodSymbol method, IMethodSymbol? parameterless, IMethodSymbol? withChildren) =>
        (parameterless is not null
            && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, parameterless))
        || (withChildren is not null
            && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, withChildren));

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

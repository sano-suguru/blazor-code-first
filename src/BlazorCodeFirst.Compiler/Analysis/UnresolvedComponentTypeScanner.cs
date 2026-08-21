using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Sweeps a design-time expression that failed to translate, reporting BCF3012 for every
/// <c>Html.Component&lt;T&gt;()</c> call whose type argument does not resolve.
/// </summary>
/// <remarks>
/// A sweep rather than a check inside <see cref="RenderExpressionAnalyzer"/>'s Component branch,
/// because the analyzer often never reaches that branch. Any call whose arguments contain a lambda,
/// <c>If</c>, <c>ForEach</c>, or a decoration applied after <c>.Param</c>, has an outer
/// <c>GetSymbolInfo</c> that degrades to a null symbol when an unresolved type is nested inside it, and
/// the analyzer exits at its early symbol guard without recursing into the lambda body. Running on the
/// failure path instead makes the report independent of where translation gave up.
/// </remarks>
internal static class UnresolvedComponentTypeScanner
{
    /// <summary>
    /// Records BCF3012 into <paramref name="context"/> for every unresolved <c>Component&lt;T&gt;()</c>
    /// type argument under <paramref name="root"/>, in syntactic order, once per invocation.
    /// </summary>
    public static void Report(ExpressionSyntax root, ViewPartBodyContext context)
    {
        var componentMethod = context.KnownSymbols.HtmlComponent;
        if (componentMethod is null)
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
                || !IsComponentFactory(method, componentMethod))
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

            // A collection expression, not ImmutableArray.Create(x): the latter is IDE0303, an error here.
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3012,
                typeArgument.GetLocation(),
                [typeArgument.ToString()]));
        }
    }

    /// <summary>
    /// Whether <paramref name="method"/> is <c>Html.Component&lt;T&gt;()</c>, the only component syntax,
    /// children arrive through <c>ComponentView&lt;T&gt;</c>'s indexer, which is not an invocation and so
    /// never reaches this sweep.
    /// </summary>
    /// <remarks>
    /// The <paramref name="parameterless"/> null guard is defensive consistency rather than a correctness
    /// requirement: <c>OriginalDefinition</c> is never null, so an unguarded comparison against a null known
    /// symbol already answers <see langword="false"/>, and <c>Report</c> returns before its loop when the
    /// symbol is absent. It is kept so callers other than <c>Report</c> cannot depend on that early exit.
    /// </remarks>
    internal static bool IsComponentFactory(IMethodSymbol method, IMethodSymbol? parameterless) =>
        parameterless is not null
        && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, parameterless);

    /// <summary>
    /// The written type-argument syntax of a <c>Component&lt;T&gt;()</c> invocation. Returns
    /// <see langword="null"/> when the invocation is not a generic call, or names more than one
    /// type argument.
    /// </summary>
    private static TypeSyntax? FindTypeArgumentSyntax(InvocationExpressionSyntax invocation)
    {
        var generic = TypeSymbolFacts.TryGetInvokedGenericName(invocation);

        // A list pattern on a SeparatedSyntaxList needs System.Index.GetOffset, unavailable on
        // netstandard2.0 (CS0656); check Count and index explicitly.
        return generic is not null && generic.TypeArgumentList.Arguments.Count == 1
            ? generic.TypeArgumentList.Arguments[0]
            : null;
    }
}

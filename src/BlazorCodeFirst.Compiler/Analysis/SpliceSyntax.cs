using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// The written shape of a spliced child list's projection, <c>&lt;source&gt;.Select(&lt;selector&gt;)</c>
/// (#172), owned in one place.
/// </summary>
/// <remarks>
/// <para>
/// Three readers ask about a splice and each answers a different <em>semantic</em> question, for reasons
/// their own comments record: <c>RenderExpressionAnalyzer.AnalyzeSplice</c> requires the call to resolve
/// to <c>Enumerable.Select</c>, <c>UnresolvedValueTypeScanner.ScanSplice</c> only excludes a call that
/// resolves to something else, and <c>UnresolvedValueTypeScanner.IsSplicedSelect</c> asks nothing of
/// symbols at all because there is none in the case it exists for. What none of them has its own reason
/// to differ on is the <em>syntax</em>, and before this type they did: two accepted any one-argument
/// member-access call while the third also required the name, so widening what folds could be applied to
/// two of the three and the third would silently stop reporting inside the new shape.
/// </para>
/// <para>
/// Matching the name as written rather than resolving it is what lets the one rule serve all three. It
/// costs nothing in precision, because every reader that cares then asks
/// <see cref="KnownSymbols.IsEnumerableSelect"/>, and it is the only test available to the reader that
/// runs when nothing binds.
/// </para>
/// <para>
/// The static spelling <c>Enumerable.Select(source, selector)</c> is deliberately not this shape. Its
/// source sits in argument space, where a named or reordered argument can move it, so reading it off the
/// syntax would mean re-deriving the argument-binding rule this analyzer asks Roslyn for everywhere else
/// (<see cref="FactoryArguments"/>). The fluent spelling has no such freedom: C# fixes the receiver's
/// position, so <see cref="MemberAccessExpressionSyntax.Expression"/> is the source by the language rule
/// rather than by a rule reimplemented here.
/// </para>
/// </remarks>
internal static class SpliceSyntax
{
    /// <summary>
    /// Whether <paramref name="expression"/> has this shape, for the reader that needs nothing out of it.
    /// </summary>
    internal static bool IsProjection(ExpressionSyntax expression) =>
        TryMatchProjection(expression, out _, out _, out _);

    /// <summary>
    /// Matches <paramref name="expression"/> as <c>&lt;source&gt;.Select(&lt;selector&gt;)</c>, reporting
    /// the invocation, the member access whose receiver is the source, and the selector's written
    /// expression.
    /// </summary>
    internal static bool TryMatchProjection(
        ExpressionSyntax expression,
        [MaybeNullWhen(false)] out InvocationExpressionSyntax invocation,
        [MaybeNullWhen(false)] out MemberAccessExpressionSyntax access,
        [MaybeNullWhen(false)] out ExpressionSyntax selector)
    {
        if (expression is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Select" } matched,
            } candidate
            && candidate.ArgumentList.Arguments.Count == 1)
        {
            invocation = candidate;
            access = matched;
            selector = candidate.ArgumentList.Arguments[0].Expression;
            return true;
        }

        invocation = null!;
        access = null!;
        selector = null!;
        return false;
    }
}

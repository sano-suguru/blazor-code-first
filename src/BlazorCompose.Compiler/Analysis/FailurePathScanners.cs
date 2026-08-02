using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Every failure-path sweep a design-time expression gets, in one place.
/// </summary>
/// <remarks>
/// <para>
/// A design-time expression has two hosts — <c>ComponentModelFactory</c> for a component's
/// <c>Body</c>/<c>Chrome</c>, <c>ComposableDefinitionFactory</c> for a <c>[Composable]</c> method body —
/// and they are separate code paths.  A check inside <c>RenderExpressionAnalyzer</c> reaches both for
/// free because both route through it.  A check in a sweep the host invokes reaches only the hosts that
/// invoke it, and that difference is invisible at the call site: each factory looks complete on its own.
/// </para>
/// <para>
/// That is not hypothetical.  BC3008 moved out of <c>RenderExpressionAnalyzer.Classify</c> into
/// <c>RejectedDecorationScanner</c> during #100, was wired into the component host only, and a misplaced
/// decoration inside a <c>[Composable]</c> body silently degraded to the generic BC1002 for the life of
/// the branch — every BC3008 test hosted its expression in <c>Body</c>, so nothing failed.  The lesson
/// recorded then was that moving a check out of a shared routine turns its host set from something
/// implied into something a human has to enumerate.  This type is the answer to that: the enumeration
/// exists once, so there is no per-host list to get wrong.
/// </para>
/// <para>
/// The order below is the order both hosts ran these three in before they were collapsed here, and it is
/// kept deliberately rather than incidentally.  It governs the sequence <see cref="ComposableBodyContext"/>
/// accumulates diagnostics in, not which diagnostics get reported: the three scanners look at disjoint
/// syntax shapes — <see cref="UnresolvedValueTypeScanner"/> skips a <c>Component&lt;T&gt;()</c> call
/// outright and leaves it to <see cref="UnresolvedComponentTypeScanner"/> — so no scanner here overrides
/// another's finding.  The one dedup that does exist, <c>ComposableBodyContext.ReportUnresolvedType</c>
/// dropping a repeat BC3015 at the same file span, only ever compares a BC3015 against an earlier BC3015;
/// it is internal to <see cref="UnresolvedValueTypeScanner"/>'s own recursion and does not make the order
/// between the three scanners load-bearing.
/// </para>
/// <para>
/// A sweep that legitimately applies to one host only cannot be expressed through <see cref="ReportAll"/>,
/// and none exists.  If one is ever needed, give it its own entry point with the reason written down —
/// quietly reintroducing per-host lists puts back the failure mode this type removes.
/// </para>
/// </remarks>
internal static class FailurePathScanners
{
    /// <summary>
    /// Sweeps <paramref name="root"/> for the specific causes behind a failed translation, so the author
    /// is told the real one instead of BC1003's "not statically analyzable".
    /// </summary>
    /// <remarks>
    /// Call only on the failure path: a healthy expression must pay nothing for these.
    /// </remarks>
    public static void ReportAll(ExpressionSyntax root, ComposableBodyContext context)
    {
        UnresolvedComponentTypeScanner.Report(root, context);
        UnresolvedValueTypeScanner.Report(root, context);
        RejectedDecorationScanner.Report(root, context);
    }
}

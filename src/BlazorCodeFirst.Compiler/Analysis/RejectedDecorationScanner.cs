using System.Linq;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Sweeps a design-time expression that failed to translate for a decoration that cannot be translated.
/// Reports BCF3008 when a decoration (<c>.Class</c>, a named attribute shortcut, an event shortcut,
/// <c>.Attr</c> or <c>.On</c>) was written on a receiver that opens no element frame, <c>Fragment(…)</c>,
/// <c>Raw(…)</c>, <c>If(…)</c>, <c>ForEach(…)</c>, <c>Component&lt;T&gt;()</c>, a <c>[ViewPart]</c> result,
/// an externally supplied <c>RenderFragment</c>, or an element that has already taken its children; and
/// BCF3026 when the name written in a decoration's position is one <c>Decorations</c> does not declare at
/// all, on a receiver that does open an element frame.
/// </summary>
/// <remarks>
/// <para>
/// The type system does express this constraint: decorations extend <c>ElementView</c>, so decorating a
/// <c>View</c> is CS1929. That C# error nevertheless never reaches the author of a component. A component
/// whose design-time expression does not translate gets no generated <c>RenderView</c>, so its class
/// necessarily carries CS0534, a declaration-stage error, and <c>csc</c> stops after the declaration stage
/// without binding method bodies. CS1929 is a body-binding diagnostic, so in the one shape that matters it
/// is only ever computed for a build that has already been abandoned. Measured against a real MSBuild
/// build, before this scanner existed: the fixture <c>Bcf3008Host</c> reported CS0534 and BCF1003, and no
/// CS1929. That same fixture reports BCF3008 today because this scanner exists, but the diagnostic
/// that never appeared is the one the argument rests on.
/// </para>
/// <para>
/// A generator diagnostic does survive that cutoff, since BCF1003 was delivered in the very same build,
/// so a
/// BlazorCodeFirst diagnostic is the only vehicle that reaches the author, and BCF3008 is the one that says
/// what is actually wrong. Without this scanner the author is told instead that the expression "uses a
/// construct that is not statically analyzable", which is both generic and untrue: the construct is
/// perfectly analyzable, and only the attributes' position is wrong.
/// </para>
/// <para>
/// Note that <c>Compilation.GetDiagnostics()</c>, the Roslyn API the in-process tests call, does not apply
/// the declaration-stage cutoff, so an in-process test can observe a CS1929 that no author ever will. That
/// is why the delivery claim is pinned in <c>tests/diagnostic-fixtures</c>, against a real build, and not
/// here.
/// </para>
/// <para>
/// Two diagnostics, one walk. They are disjoint by construction: BCF3008 requires
/// <see cref="KnownSymbols.DeclaresDecorationNamed"/> to hold and BCF3026 requires it to fail, so no
/// invocation can match both and neither has to be ordered against the other. Sharing the walk rather than
/// adding a second scanner to <see cref="FailurePathScanners"/> keeps the host enumeration that type's
/// remarks warn about from growing, and keeps a failed body paying one descendant traversal.
/// </para>
/// <para>
/// A sweep on the failure path rather than a check inside <see cref="RenderExpressionAnalyzer"/>, for the
/// same reason as its two neighbours: the analyzer never reaches its decoration arm for this shape. The
/// invocation binds to no decoration at all, so <c>Classify</c>'s method arm returns null before the
/// decoration branch, which is why that branch's <c>ElementTemplateNode</c> guard is now unreachable and
/// carries no report of its own.
/// </para>
/// </remarks>
internal static class RejectedDecorationScanner
{
    /// <summary>
    /// Records at most one BCF3008 and at most one BCF3026 into <paramref name="context"/> for the whole of
    /// <paramref name="root"/>, each at the member name of the innermost invocation that matched it.
    /// </summary>
    /// <remarks>
    /// One report per body, not one per decoration: <c>Fragment("a").Class("x").Id("y")</c> is a single
    /// mistake, and the innermost failing decoration is the one whose receiver is the non-element, so its
    /// span points at where the chain first went wrong. Everything further out in such a chain binds
    /// cleanly and is skipped by <see cref="IsMisplacedDecoration"/> anyway, Roslyn's error recovery gives
    /// the failed call the return type of the decoration it reached for, so <c>.Id</c> above sees an
    /// <c>ElementView</c> receiver and resolves, but the containment test below keeps the guarantee
    /// independent of that recovery, and also holds it to one report when a body carries two unrelated
    /// misplaced decorations.
    /// </remarks>
    public static void Report(ExpressionSyntax root, ViewPartBodyContext context)
    {
        // Every required clause below is a comparison against one of these three, so with the bracket surface
        // absent from the referenced runtime there is nothing to compare and nothing to report. The one
        // optional receiver, RenderFragment, carries its own null guard at its use site instead, because a
        // compilation can lack it while the bracket surface is present.
        var symbols = context.KnownSymbols;
        if (symbols.ElementViewType is null || symbols.ViewType is null || symbols.ComponentViewType is null)
            return;

        InvocationExpressionSyntax? misplaced = null;
        InvocationExpressionSyntax? unknown = null;

        foreach (var invocation in root.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // Descendants are visited after their ancestors, so a nested match refines the one already held;
            // a match in an unrelated subtree does not, and the first mistake in source order stays.
            if (IsMisplacedDecoration(invocation, context))
            {
                if (misplaced is null || misplaced.Span.Contains(invocation.Span))
                    misplaced = invocation;
            }
            else if (IsUnknownDecorationName(invocation, context))
            {
                if (unknown is null || unknown.Span.Contains(invocation.Span))
                    unknown = invocation;
            }
        }

        // Reported at the member name, `Class`, not the whole chain, because the name is what is in the
        // wrong place. A collection expression, not ImmutableArray.Create(): the latter is IDE0303, an
        // error in this repo.
        if (misplaced?.Expression is MemberAccessExpressionSyntax misplacedAccess)
        {
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3008, misplacedAccess.Name.GetLocation(), []));
        }

        // Same anchor for the same reason, and the name is also the message's one argument: a report that
        // did not repeat it would read as a complaint about the whole chain.
        if (unknown?.Expression is MemberAccessExpressionSyntax unknownAccess)
        {
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3026,
                unknownAccess.Name.GetLocation(),
                [unknownAccess.Name.Identifier.ValueText]));
        }
    }

    /// <summary>
    /// Whether <paramref name="invocation"/> is a decoration written on a design-time node that opens no
    /// element frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three conjuncts, and none of them is sufficient alone. The written name must be one the referenced
    /// runtime's <c>Decorations</c> type actually declares; the call must have reached for something that
    /// returns <em>our</em> <c>ElementView</c>; and it must have been written on <em>our</em>
    /// <c>View</c> or <c>ComponentView&lt;T&gt;</c>, or on Blazor's <c>RenderFragment</c>. The two type tests
    /// are symbol identity. The name test is not, and is exactly why the other two are required: a spelling
    /// proves nothing on its own, since any type may declare a <c>Class</c> or a <c>Title</c>. Its own names
    /// come from symbols resolved out of the referenced runtime assembly, so a user-defined
    /// <c>Some.BlazorCodeFirst.Decorations</c> contributes none of them, see
    /// <see cref="KnownSymbols.DeclaresDecorationNamed"/>. A name test is admissible here only because the
    /// call did not resolve, so there is no symbol to classify; every recognition of a call that did resolve
    /// asks <see cref="KnownSymbols.ClassifySurfaceMethod"/> instead.
    /// </para>
    /// <para>
    /// <c>RenderFragment</c> is named in the receiver test rather than covered by <c>View</c>, even though it
    /// converts to <c>View</c> implicitly. An extension-method receiver admits only identity, reference and
    /// boxing conversions, never a user-defined one, so a <c>RenderFragment</c> never becomes a <c>View</c>
    /// for the purpose of resolving <c>.Class</c>, which is exactly why the call fails, and also why testing
    /// the receiver against <c>View</c> alone would never see it. The author's mistake is the one BCF3008
    /// already names: <c>DESIGN.md</c> groups an externally supplied <c>RenderFragment</c> with
    /// <c>Fragment(…)</c> and <c>Raw(…)</c> as content that opens no element frame, and decorating any of the
    /// three is the same error.
    /// </para>
    /// <para>
    /// The name conjunct is load-bearing, not belt-and-braces. The type tests describe a decoration's
    /// <em>signature</em>, not a decoration: a third-party or user-declared
    /// <c>static ElementView Wrap(this View content, string tag)</c> has exactly that signature, and
    /// without the name test a wrong-argument call to it was reported as a misplaced decoration, measured,
    /// not hypothesized. What remains after the narrowing is a method that shares a decoration's signature
    /// <em>and</em> its name; such a method is a decoration in all but declaration site, so BCF3008's advice
    /// still fits it, but that is the residual boundary rather than an absence of one.
    /// </para>
    /// <para>
    /// The failed call's type comes from Roslyn's error recovery, which gives an unresolved invocation the
    /// return type of the best candidate it considered. That is the only trace of the intended decoration
    /// the semantic model leaves: <c>GetSymbolInfo</c> reports no candidates at all for a receiver that an
    /// extension method cannot take (measured on every shape BCF3008 covers), and where a conversion applies
    /// to the failed call it reports that conversion instead. Under-reporting is the failure mode: if
    /// recovery ever picks some other candidate the shape simply falls through to BCF1003, which is the
    /// right way round for a diagnostic that names a specific mistake.
    /// </para>
    /// <para>
    /// A call that resolved is not this diagnostic, whatever its receiver: a user-declared extension on
    /// <c>View</c> returning an <c>ElementView</c> is legal, compiles, and must not be blamed for a
    /// failure elsewhere in the body. Only a conversion is discounted, because that is what
    /// <c>GetSymbolInfo</c> reports in place of a call that did not bind; a call that did bind keeps its own
    /// symbol even where a conversion is applied on top of it. The <em>unresolved</em> branch of that same
    /// shape is what the name conjunct above covers.
    /// </para>
    /// </remarks>
    private static bool IsMisplacedDecoration(
        InvocationExpressionSyntax invocation, ViewPartBodyContext context)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var symbols = context.KnownSymbols;

        // First because it is the only conjunct that asks nothing of the semantic model.
        if (!symbols.DeclaresDecorationNamed(memberAccess.Name.Identifier.ValueText))
            return false;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is IMethodSymbol bound
            && bound.MethodKind != MethodKind.Conversion)
        {
            return false;
        }

        var reached = context.SemanticModel.GetTypeInfo(invocation, context.CancellationToken).Type;
        if (!SymbolEqualityComparer.Default.Equals(reached, symbols.ElementViewType))
            return false;

        var receiverType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (receiverType is null)
            return false;

        return SymbolEqualityComparer.Default.Equals(receiverType, symbols.ViewType)
            || SymbolEqualityComparer.Default.Equals(
                receiverType.OriginalDefinition, symbols.ComponentViewType)
            || (symbols.RenderFragmentType is { } renderFragmentType
                && SymbolEqualityComparer.Default.Equals(receiverType, renderFragmentType));
    }

    /// <summary>
    /// Whether <paramref name="invocation"/> writes a name in a decoration's position, on a receiver that
    /// does open an element frame, that <c>Decorations</c> does not declare.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The receiver test is symbol identity against our own <c>ElementView</c>, and it is what separates this
    /// from every other call in a failed body: <c>Helper()</c> and <c>item.Title.ToUpper()</c> have no such
    /// receiver and are not decorations that went wrong. The name test is the complement of
    /// <see cref="IsMisplacedDecoration"/>'s, so the two conditions cannot both hold.
    /// </para>
    /// <para>
    /// Only the unbound shape is covered here, the misspelling that binds to nothing. A call that does bind
    /// falls through to BCF1003 exactly as before.
    /// </para>
    /// </remarks>
    private static bool IsUnknownDecorationName(
        InvocationExpressionSyntax invocation, ViewPartBodyContext context)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        var symbols = context.KnownSymbols;

        // First because it is the only conjunct that asks nothing of the semantic model.
        if (symbols.DeclaresDecorationNamed(memberAccess.Name.Identifier.ValueText))
            return false;

        var receiverType = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (!SymbolEqualityComparer.Default.Equals(receiverType, symbols.ElementViewType))
            return false;

        return context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is null;
    }
}

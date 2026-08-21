using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Sweeps a design-time expression that failed to translate for an element written as a simple name that a
/// declaration nearer than <c>Html</c> took. Reports BCF3027.
/// </summary>
/// <remarks>
/// <para>
/// <c>using static BlazorCodeFirst.Html;</c> brings every curated helper into simple-name scope, and a
/// declaration closer than that import wins the lookup. Where a member's own type is indexable the element
/// expression stays legal C#, and silently becomes an indexer call on that member:
/// <c>Div[Data["Heading"]]</c> against a <c>string Data</c> reads <c>Data</c>'s character indexer and asks
/// it for the index <c>"Heading"</c>.
/// </para>
/// <para>
/// The C# error for that is CS1503, "cannot convert from 'string' to 'int'", which names neither the
/// element, nor the member that took its place, nor the fix. It does not reach the author in any case, for
/// the reason Appendix A A.0 gives and <see cref="RejectedDecorationScanner"/>'s remarks work through for CS1929:
/// the body does not translate, so no <c>RenderView</c> is generated, so the class carries CS0534, a
/// declaration-stage error, and <c>csc</c> stops before it binds method bodies. What the author was left
/// with is BCF1003's "uses a construct that is not statically analyzable", said of an expression that is
/// perfectly analyzable and merely bound to the wrong thing (#127).
/// </para>
/// <para>
/// Widening the curated set from 22 helpers to 100 (#99) did not create this failure but moved it from rare
/// to routine: <c>Code</c>, <c>Data</c>, <c>Label</c>, <c>Summary</c>, <c>Source</c>, <c>Input</c>,
/// <c>Option</c>, <c>Form</c> and <c>Select</c> are ordinary Blazor parameter names.
/// </para>
/// <para>
/// A member is one of four things that take the name, and the sweep asks for none of them in particular: a
/// type, a namespace and a method group reach it too, each with a C# error of its own that the same cutoff
/// suppresses. Why the four are one id rather than four, and which C# error each carries, is on the
/// descriptor (#266).
/// </para>
/// <para>
/// The two conjuncts read different tables — the name from the compiler's own
/// <c>KnownSymbols.CuratedTags</c>, the identity from the helpers resolved out of the referenced runtime —
/// and where those disagree a declaration spelled like a helper the runtime does not declare shadows nothing
/// and is reported all the same. <c>KnownSymbolsSyncTests.ElementTags_AreExactlyTheCuratedSet</c> holds the two
/// against each other, and the analyzer ships inside the runtime package, so reaching the residue takes a
/// hand-mixed pair of versions.
/// </para>
/// <para>
/// Its own scanner rather than a second walk inside <see cref="RejectedDecorationScanner"/>: that type
/// shares one walk between BCF3008 and BCF3026 because those two classify the <em>same</em> node, an
/// invocation in a decoration's position, on complementary conditions. This one classifies element accesses,
/// a disjoint syntax shape, and folding it in would give that type two unrelated questions to answer rather
/// than one question with two answers.
/// </para>
/// </remarks>
internal static class ShadowedElementHelperScanner
{
    /// <summary>
    /// Records at most one BCF3027 into <paramref name="context"/> for the whole of <paramref name="root"/>,
    /// at the shadowed receiver identifier.
    /// </summary>
    /// <remarks>
    /// One report per body, like its two neighbours, and the first in source order rather than the innermost:
    /// each shadowed name is its own independent lookup, so there is no chain here whose first link explains
    /// the rest. The location is the receiver because the fix is to qualify it, <c>Html.Data["Heading"]</c>,
    /// and no additional location is attached for the shadowing declaration — no descriptor in
    /// <c>DiagnosticDescriptors</c> uses one, and this is not the diagnostic to change that convention on.
    /// </remarks>
    public static void Report(ExpressionSyntax root, ViewPartBodyContext context)
    {
        // With no resolved helper table there is nothing that could have been shadowed, and every conjunct
        // below would degrade the wrong way: an empty ElementTags makes the identity test fail for every
        // name, so an unguarded sweep would report each correctly bound element against a runtime that
        // merely lacks the bracket surface.
        var symbols = context.KnownSymbols;
        if (symbols.ElementTags.Count == 0)
            return;

        foreach (var access in root.DescendantNodesAndSelf().OfType<ElementAccessExpressionSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            // A simple name only. `Html.Data[…]` is the documented fix and shadows no lookup, and
            // `this.Data[…]` or `_map[…]` never reached for an element in the first place.
            if (access.Expression is not IdentifierNameSyntax receiver)
                continue;

            var name = receiver.Identifier.ValueText;

            // First because it is the only conjunct that asks nothing of the semantic model. #68 planned to
            // reuse it as its own prefilter and did not need to: BCF3029 keys on resolved symbols
            // throughout, where a name test would have been the cheap half of a syntax-wide sweep it does
            // not run. Both curated vocabularies are checked (#319 / #534): a name spelled like an Svg
            // helper is just as reachable through `using static Svg;` as an Html one.
            var isHtmlName = KnownSymbols.IsCuratedHelperName(name);
            var isSvgName = KnownSymbols.IsCuratedSvgHelperName(name);
            if (!isHtmlName && !isSvgName)
                continue;

            var shadowing = Shadowing(
                context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken));

            // No one declaration took the name. Either nothing in scope answers to it, a missing
            // `using static BlazorCodeFirst.Html;` that leaves the element unresolved, or several do and
            // Shadowing declines to pick one; BCF1003 is the honest report for both.
            if (shadowing is null)
                continue;

            // Symbol identity against the helpers resolved out of the referenced runtime, not a second name
            // test: the name test above says only that the identifier is spelled like a helper, and what
            // decides this diagnostic is whether it reached one.
            if (shadowing is IPropertySymbol property && symbols.IsElementHelper(property))
                continue;

            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3027, receiver.GetLocation(), [name, Describe(shadowing), Fix(name, isHtmlName, isSvgName)]));
            return;
        }
    }

    /// <summary>
    /// The one declaration <paramref name="info"/> says the receiver reached, or null where no single
    /// declaration answers to the name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a value binds, so <see cref="SymbolInfo.Symbol"/> alone reaches the member shape and no other.
    /// The rest arrive as candidates, which is measured rather than assumed (#266): a type or a namespace
    /// answers with <c>CandidateReason.NotAValue</c> and a method group with
    /// <c>OverloadResolutionFailure</c>, each carrying the declaration that won the lookup. The reason is
    /// deliberately not read — every reason for which a candidate exists is a reason the name reached that
    /// declaration instead of the helper, and enumerating them would be a list to keep current against
    /// Roslyn for no gain.
    /// </para>
    /// <para>
    /// A candidate set of two or more is left alone. That is the ambiguous lookup, where the report could
    /// not say which declaration took the name, and where the helper itself may be one of the two — the
    /// answer <c>Html.<em>Name</em></c> is right there, but naming a shadowing declaration would not be.
    /// </para>
    /// </remarks>
    private static ISymbol? Shadowing(SymbolInfo info) =>
        info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);

    /// <summary>
    /// What <paramref name="shadowing"/> is, for the message to name it by. One word, in the author's
    /// vocabulary rather than Roslyn's, to say what they have to go and look for. Everything that is not a
    /// type, a namespace or a method is called a member — a property or a field, and equally a local or a
    /// lambda parameter, which the surface reaches through a <c>ForEach</c> or an <c>If</c> body.
    /// </summary>
    private static string Describe(ISymbol shadowing) => shadowing switch
    {
        ITypeSymbol => "type",
        INamespaceSymbol => "namespace",
        IMethodSymbol => "method",
        _ => "member",
    };

    /// <summary>
    /// The qualified spelling(s) BCF3027 suggests as the fix, for <paramref name="name"/> curated in
    /// <c>Html</c> (<paramref name="isHtmlName"/>), <c>Svg</c> (<paramref name="isSvgName"/>), or both
    /// (#319 / #534).
    /// </summary>
    /// <remarks>
    /// Both, not a guess at one: <c>Html</c> and <c>Svg</c> curate several identical names (<c>A</c>,
    /// <c>Audio</c>, <c>Canvas</c>, <c>Iframe</c>, <c>Video</c>), and a declaration that shadows the name
    /// shadows both equally — C# simple-name lookup binds the nearer declaration before it ever consults
    /// either `using static` import, so which vocabulary the author meant is not recoverable from this
    /// shape. The dual case embeds its own closing and opening single quotes, since the descriptor's
    /// <c>messageFormat</c> wraps this return value in one quoted pair (<c>write '{2}' to name</c>) and has
    /// no second slot for a two-suggestion message.
    /// </remarks>
    private static string Fix(string name, bool isHtmlName, bool isSvgName) =>
        (isHtmlName, isSvgName) switch
        {
            (true, true) => $"Html.{name}' or 'Svg.{name}",
            (true, false) => $"Html.{name}",
            _ => $"Svg.{name}",
        };
}

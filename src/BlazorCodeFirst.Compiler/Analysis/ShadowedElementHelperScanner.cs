using System.Linq;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Sweeps a design-time expression that failed to translate for an element written as a simple name that a
/// member of the enclosing code shadows. Reports BCF3027.
/// </summary>
/// <remarks>
/// <para>
/// <c>using static BlazorCodeFirst.Html;</c> brings every curated helper into simple-name scope, and a
/// member declared closer wins that lookup over the imported property. Where the member's own type is
/// indexable the element expression stays legal C#, and silently becomes an indexer call on that member:
/// <c>Div[Data["Heading"]]</c> against a <c>string Data</c> reads <c>Data</c>'s character indexer and asks
/// it for the index <c>"Heading"</c>.
/// </para>
/// <para>
/// The C# error for that is CS1503, "cannot convert from 'string' to 'int'", which names neither the
/// element, nor the member that took its place, nor the fix. It does not reach the author in any case, for
/// the reason 付録A A.0 gives and <see cref="RejectedDecorationScanner"/>'s remarks work through for CS1929:
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
/// A type that shadows a helper is outside this diagnostic, and that is a known gap rather than a chosen
/// boundary: #127 left it out because C# raises CS0119 for it, and CS0119 turns out not to reach the author
/// either, by the same cutoff as everything above. #266 carries the measurement and the open question.
/// </para>
/// <para>
/// The two conjuncts read different tables — the name from the compiler's own
/// <c>KnownSymbols.CuratedTags</c>, the identity from the helpers resolved out of the referenced runtime —
/// and where those disagree a member spelled like a helper the runtime does not declare shadows nothing and
/// is reported all the same. <c>KnownSymbolsSyncTests.ElementTags_AreExactlyTheCuratedSet</c> holds the two
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
    /// and no additional location is attached for the shadowing member's declaration — no descriptor in
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
            // not run.
            if (!KnownSymbols.IsCuratedHelperName(name))
                continue;

            var bound = context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol;

            // The receiver has to name a value. Roslyn answers null here for both shapes that do not: a
            // missing `using static BlazorCodeFirst.Html;`, which is no shadowing at all, and the
            // type-shadows-a-helper case the remarks above leave out. Which means the ITypeSymbol arm never
            // fires — measured: once the element access fails to bind, the identifier alone carries no
            // symbol either. It is written out all the same so the exclusion is stated in the code rather
            // than resting on that answer, since a type reaching the report below would be told it is a
            // member. It is also where #266's widening would start: covering the type case needs another
            // route to the shadowing declaration, because this one does not reach it.
            if (bound is null or ITypeSymbol)
                continue;

            // Symbol identity against the helpers resolved out of the referenced runtime, not a second name
            // test: the name test above says only that the identifier is spelled like a helper, and what
            // decides this diagnostic is whether it reached one.
            if (bound is IPropertySymbol property && symbols.IsElementHelper(property))
                continue;

            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3027, receiver.GetLocation(), [name]));
            return;
        }
    }
}

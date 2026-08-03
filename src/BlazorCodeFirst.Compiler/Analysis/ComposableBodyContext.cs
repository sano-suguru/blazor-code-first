using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Carries the shared state required to normalize the expressions inside a single composable
/// definition body: the semantic model, the containing type, the parameter-to-ordinal map, the
/// resolved runtime symbols, and the accumulating access-requirement and diagnostic builders.
/// </summary>
internal sealed class ComposableBodyContext
{
    private readonly ImmutableDictionary<ISymbol, int> _parameterOrdinals;
    private readonly Dictionary<ISymbol, int> _iterationOverlay =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<TextSpan> _rejectedValueRouteSpans = [];
    private int _iterationDepth;

    public ComposableBodyContext(
        SemanticModel semanticModel,
        INamedTypeSymbol containingType,
        string methodDisplayName,
        KnownSymbols knownSymbols,
        ImmutableDictionary<ISymbol, int> parameterOrdinals,
        CancellationToken cancellationToken)
    {
        SemanticModel = semanticModel;
        ContainingType = containingType;
        MethodDisplayName = methodDisplayName;
        KnownSymbols = knownSymbols;
        _parameterOrdinals = parameterOrdinals;
        CancellationToken = cancellationToken;
        AccessRequirements = ImmutableArray.CreateBuilder<ComposableAccessRequirement>();
        Diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
    }

    public SemanticModel SemanticModel { get; }

    public INamedTypeSymbol ContainingType { get; }

    public string MethodDisplayName { get; }

    public KnownSymbols KnownSymbols { get; }

    public CancellationToken CancellationToken { get; }

    public ImmutableArray<ComposableAccessRequirement>.Builder AccessRequirements { get; }

    /// <summary>
    /// All diagnostics recorded while normalizing this body, both errors (for example BCF1002) and
    /// warnings (for example BCF3002). Definition/model validity is gated on <see cref="DiagnosticInfo.IsError"/>
    /// severity, not on this collection being non-empty, so a warning-only body still builds successfully
    /// and surfaces its warnings.
    /// </summary>
    public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

    /// <summary>
    /// The expression that failed to translate, or <see langword="null"/> if none did. Set by
    /// <see cref="RenderExpressionAnalyzer.Analyze"/> on every failed classification, keeping only the
    /// first: recursion is depth-first and no caller recovers from a <see langword="null"/> child, so the
    /// first failure recorded is the innermost one and every enclosing failure is a consequence of it.
    /// This is what gives BCF1003 a location: it is raised after classification, where no syntax remains.
    /// </summary>
    /// <remarks>
    /// "First" resolves depth, not source order. Between siblings at the same depth it is analysis order
    /// that wins, and those differ where arguments are named: <c>If(c, otherwise: o, then: t)</c> analyzes
    /// <c>then</c> first and never reaches <c>otherwise</c>, so <c>t</c> is blamed even though <c>o</c> is
    /// written earlier. Both are genuinely untranslatable, so this picks between real culprits rather
    /// than mislocating one. Note that ordering by source position instead would invert the rule this
    /// exists for: recording runs bottom-up, and an enclosing design-time syntax call always starts before
    /// the expression nested in it, so the outermost failure would win every time.
    /// </remarks>
    public Location? UntranslatableLocation { get; private set; }

    /// <summary>
    /// Records <paramref name="expression"/> as the reason this body could not be translated, unless a
    /// deeper expression already claimed that role.
    /// </summary>
    public void RecordUntranslatable(SyntaxNode expression) =>
        UntranslatableLocation ??= expression.GetLocation();

    public bool TryGetParameterOrdinal(ISymbol symbol, out int ordinal)
    {
        if (_parameterOrdinals.TryGetValue(symbol, out ordinal))
            return true;
        return _iterationOverlay.TryGetValue(symbol, out ordinal);
    }

    /// <summary>
    /// Registers a ForEach iteration variable (its content and key lambda parameters denote the same
    /// loop variable) at the next free ordinal, base parameter count plus current nesting depth, so a
    /// reference to it becomes a parameter hole at that ordinal. Returns that ordinal.
    /// </summary>
    public int PushIterationVariable(ISymbol contentParameter, ISymbol keyParameter)
    {
        var ordinal = _parameterOrdinals.Count + _iterationDepth;
        _iterationOverlay[contentParameter] = ordinal;
        _iterationOverlay[keyParameter] = ordinal;
        _iterationDepth++;
        return ordinal;
    }

    /// <summary>Removes an iteration variable registered by <see cref="PushIterationVariable"/>.</summary>
    public void PopIterationVariable(ISymbol contentParameter, ISymbol keyParameter)
    {
        _iterationOverlay.Remove(contentParameter);
        _iterationOverlay.Remove(keyParameter);
        _iterationDepth--;
    }

    /// <summary>
    /// Records a distinct accessibility requirement for a referenced member/type so expansion can
    /// reject inlining into a site that cannot legally name it.
    /// </summary>
    public void AddAccessRequirement(ComposableAccessRequirement requirement)
    {
        foreach (var existing in AccessRequirements)
        {
            if (existing == requirement)
                return;
        }

        AccessRequirements.Add(requirement);
    }

    /// <summary>
    /// Records that normal route analysis reached an invocation but rejected it before its own dynamic
    /// value was normalized. The failure scanner may still recurse into the receiver, whose diagnostics
    /// are independent, but must not infer that the rejected invocation's value would be emitted.
    /// </summary>
    public void RejectUnresolvedValueRecovery(TextSpan invocationSpan) =>
        _rejectedValueRouteSpans.Add(invocationSpan);

    public bool ShouldRecoverUnresolvedValue(TextSpan invocationSpan) =>
        !_rejectedValueRouteSpans.Contains(invocationSpan);

    /// <summary>
    /// Records a single declaration-time BCF1002 for a body that references a symbol which cannot exist
    /// in generated component code (for example a local function or a local declared in an enclosing
    /// scope). Only the first such reference is reported so a body yields exactly one declaration
    /// diagnostic regardless of how many unsupported references it contains. The dedup guard is
    /// id-specific: it only suppresses a second BCF1002, so it never drops an unrelated diagnostic (for
    /// example a co-located BCF3002 warning) that was recorded first.
    /// </summary>
    public void ReportUnsupportedReference(Location location, string reason)
    {
        foreach (var existing in Diagnostics)
        {
            if (existing.Id == DiagnosticDescriptors.BCF1002.Id)
                return;
        }

        Diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticDescriptors.BCF1002,
            location,
            [MethodDisplayName, reason]));
    }

    public void ReportUnresolvedType(Location location, string typeName)
    {
        var path = location.GetLineSpan().Path ?? string.Empty;
        foreach (var existing in Diagnostics)
        {
            if (existing.Id == DiagnosticDescriptors.BCF3015.Id
                && existing.FilePath == path
                && existing.Span == location.SourceSpan)
            {
                return;
            }
        }

        Diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticDescriptors.BCF3015,
            location,
            [typeName]));
    }
}

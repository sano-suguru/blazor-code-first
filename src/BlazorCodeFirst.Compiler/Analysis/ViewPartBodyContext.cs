using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>What a symbol referenced inside a view part body denotes.</summary>
/// <remarks>
/// The kind travels with the ordinal rather than being decided by which lookup method a caller reached
/// for. Two lookups, one that admitted content and one that did not, put the choice on every caller and
/// the pre-existing one — <see cref="ExpressionTemplateFactory"/>, which lowers value expressions — would
/// have got it wrong: it would have minted a value hole at a content ordinal, whose substituted code does
/// not exist.
/// </remarks>
internal enum BodyHoleKind
{
    /// <summary>Not a hole: an ordinary symbol the expression keeps as written.</summary>
    None,

    /// <summary>A value, substituted as code: a view part parameter or a scoped render variable.</summary>
    Value,

    /// <summary>
    /// Caller-supplied content, substituted as a node subtree: <c>Html.Slot</c> or a <c>View</c> parameter
    /// (#34). It has no expression text, so a value position must refuse it rather than lower it.
    /// </summary>
    Content,
}

/// <summary>
/// Carries the shared state required to normalize the expressions inside a single view part
/// definition body: the semantic model, the containing type, the parameter-to-ordinal map and scoped
/// render-variable overlay, the resolved runtime symbols, and the accumulating access-requirement and
/// diagnostic builders.
/// </summary>
internal sealed class ViewPartBodyContext
{
    private readonly ImmutableDictionary<ISymbol, int> _parameterOrdinals;

    /// <summary>
    /// Which of <see cref="_parameterOrdinals"/>' ordinals hold caller content rather than a value: the
    /// <c>View</c>-typed parameters and the slot (#34). Ordinals rather than symbols, because that is the form
    /// every hole carries and the form <see cref="ResolveHole"/> has in hand once the dictionary has answered.
    /// </summary>
    private readonly ImmutableHashSet<int> _contentOrdinals;

    private readonly Dictionary<ISymbol, int> _renderVariableOverlay =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<TextSpan> _rejectedValueRouteSpans = [];
    private readonly Dictionary<ISymbol, bool> _surfaceBuiltCallees = new(SymbolEqualityComparer.Default);
    private int _renderVariableDepth;

    public ViewPartBodyContext(
        SemanticModel semanticModel,
        INamedTypeSymbol containingType,
        string methodDisplayName,
        KnownSymbols knownSymbols,
        ImmutableDictionary<ISymbol, int> parameterOrdinals,
        CancellationToken cancellationToken,
        ImmutableHashSet<int>? contentOrdinals = null)
    {
        SemanticModel = semanticModel;
        ContainingType = containingType;
        MethodDisplayName = methodDisplayName;
        KnownSymbols = knownSymbols;
        _parameterOrdinals = parameterOrdinals;
        _contentOrdinals = contentOrdinals ?? [];
        CancellationToken = cancellationToken;
        AccessRequirements = ImmutableArray.CreateBuilder<ViewPartAccessRequirement>();
        Diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
    }

    public SemanticModel SemanticModel { get; }

    public INamedTypeSymbol ContainingType { get; }

    public string MethodDisplayName { get; }

    public KnownSymbols KnownSymbols { get; }

    public CancellationToken CancellationToken { get; }

    public ImmutableArray<ViewPartAccessRequirement>.Builder AccessRequirements { get; }

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

    /// <summary>
    /// What <paramref name="symbol"/> denotes in this body, and at which substitution ordinal.
    /// </summary>
    /// <remarks>
    /// One lookup answering both the ordinal and its kind, rather than one method per kind. The kind is a
    /// property of the ordinal, so putting it in the answer keeps every caller from having to know which
    /// question to ask: a value position that reached for the content-admitting lookup would mint a hole
    /// whose substituted code does not exist (#34).
    /// <para>
    /// A scoped render variable is always <see cref="BodyHoleKind.Value"/>. It holds a runtime value under a
    /// generated name, so a <c>ForEach</c> over a sequence of <c>View</c>s binds a <c>View</c>-typed loop
    /// variable that is a value and not caller content; the overlay's ordinals are appended after the
    /// parameters, so they fall outside <see cref="_contentOrdinals"/> by construction rather than by a test.
    /// </para>
    /// </remarks>
    public BodyHoleKind ResolveHole(ISymbol symbol, out int ordinal)
    {
        if (_parameterOrdinals.TryGetValue(symbol, out ordinal))
            return _contentOrdinals.Contains(ordinal) ? BodyHoleKind.Content : BodyHoleKind.Value;

        return _renderVariableOverlay.TryGetValue(symbol, out ordinal)
            ? BodyHoleKind.Value
            : BodyHoleKind.None;
    }

    /// <summary>
    /// Registers one scoped render variable at the next free ordinal. Multiple symbols may denote the
    /// same variable, as the content and key parameters of a <c>ForEach</c> do. A reference to any of the
    /// symbols becomes a parameter hole at the returned ordinal.
    /// </summary>
    public int PushRenderVariable(params ISymbol[] symbols)
    {
        var ordinal = _parameterOrdinals.Count + _renderVariableDepth;
        foreach (var symbol in symbols)
            _renderVariableOverlay[symbol] = ordinal;
        _renderVariableDepth++;
        return ordinal;
    }

    /// <summary>Removes a scoped render variable registered by <see cref="PushRenderVariable"/>.</summary>
    public void PopRenderVariable(params ISymbol[] symbols)
    {
        foreach (var symbol in symbols)
            _renderVariableOverlay.Remove(symbol);
        _renderVariableDepth--;
    }

    /// <summary>
    /// The memo for "does this callee's body build its <c>View</c> from the design-time surface", the
    /// predicate BCF3030 turns on.
    /// </summary>
    /// <remarks>
    /// The answer is a property of the callee alone, and deciding it walks that callee's whole declaration,
    /// binding every candidate node. A body calling one helper in five places would otherwise pay that walk
    /// five times, on every keystroke. Scoped to this context, which is per body and never reaches the
    /// cached incremental model, so no symbol escapes into it.
    /// <para>
    /// A lookup pair rather than a compute delegate: the computation needs this context, so a delegate
    /// would capture it and allocate a closure at every call site it exists to make cheaper.
    /// </para>
    /// </remarks>
    public bool TryGetSurfaceBuiltCallee(IMethodSymbol method, out bool answer) =>
        _surfaceBuiltCallees.TryGetValue(method, out answer);

    /// <summary>Records the answer <see cref="TryGetSurfaceBuiltCallee"/> will give from now on.</summary>
    public void RecordSurfaceBuiltCallee(IMethodSymbol method, bool answer) =>
        _surfaceBuiltCallees[method] = answer;

    /// <summary>
    /// Records a distinct accessibility requirement for a referenced member/type so expansion can
    /// reject inlining into a site that cannot legally name it.
    /// </summary>
    public void AddAccessRequirement(ViewPartAccessRequirement requirement)
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

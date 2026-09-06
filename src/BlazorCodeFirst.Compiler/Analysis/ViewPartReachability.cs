using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Computes, per component, the deliberately over-approximated subset of <see cref="ViewPartRegistry"/>
/// and <see cref="CssScopeRegistry"/> entries that <see cref="ComponentModelFactory.Expand"/> can ever
/// read for that component. This is the projection issue #480 measures as a cheaper alternative to
/// dependency tracking: instead of every component's <c>Expand</c> re-pairing with the whole registry
/// on any registry edit (the broadcast <c>RegistryBroadcastCostTests</c> measures), it re-pairs only
/// with entries it can actually reach, so an edit to an unrelated view part leaves its subset value-equal
/// and lets <c>Expand</c> report <c>Cached</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over-approximation, not exact reads.</b> <c>Expand</c> short-circuits at several guards inside
/// <c>ViewPartExpander.ExpandCall</c> (an unresolved call, a cycle, a missing access requirement, an
/// argument-binding failure, an ordinal-range violation), so the keys it actually reads on a given run are
/// a subset of what this walk reaches. Over-including a key only costs an unnecessary invalidation on an
/// edit to that view part; under-including one would miss a registry lookup <c>Expand</c> actually makes,
/// producing a spurious BCF1002 at the call site. Every guard is deliberately ignored here so the walk
/// only ever over-approximates.
/// </para>
/// <para>
/// <b>Reuses <see cref="KeyabilityResolver.Children"/></b> rather than a second traversal: that is
/// already the one exhaustive child walk over this node hierarchy, and it fails loudly
/// (<see cref="System.NotSupportedException"/>) on a node type it does not know, so a future
/// <see cref="RenderNode"/> case cannot silently under-approximate here either.
/// </para>
/// <para>
/// <b>Monotone visited set, not a push/pop active stack.</b> <see cref="KeyabilityResolver"/>'s
/// <c>ResolveCall</c> uses a push/pop <c>activeKeys</c> set because root resolution is path-sensitive:
/// the same view part can resolve to a different root depending on the call path above it. Reachability
/// is not path-sensitive: a view part is either reachable or it is not. A monotone <c>visited</c> set
/// both terminates cycles and dedupes a diamond-shaped call graph in one pass; a push/pop stack would
/// revisit shared sub-closures once per path to them.
/// </para>
/// </remarks>
internal static class ViewPartReachability
{
    /// <summary>
    /// Computes <paramref name="analysis"/>'s reachable subset of <paramref name="registry"/> and
    /// <paramref name="cssScopes"/>, deterministically ordered so the result composes into
    /// <see cref="ViewPartRegistry.Create"/>/<see cref="CssScopeRegistry.Create"/> and compares
    /// value-equal to any other subset built from the same reachable keys, regardless of walk order.
    /// Returned as <see cref="EquatableArray{T}"/>, not a plain <see cref="ImmutableArray{T}"/>: a
    /// caller wiring this into an incremental pipeline stage needs this stage's own output to be
    /// value-equal across a re-run, and a plain <c>ImmutableArray</c> compares by backing-array
    /// reference. Building the registries themselves is left to the caller's next stage, deliberately:
    /// a component whose reachable set is unchanged should let that next stage go <c>Cached</c> rather
    /// than pay for a rebuilt <see cref="ViewPartRegistry"/>/<see cref="CssScopeRegistry"/> (dictionary
    /// construction and all) on every edit regardless of relevance.
    /// </summary>
    public static (EquatableArray<ViewPartDefinitionEntry> ViewParts, EquatableArray<CssScopeEntry> CssScopes)
        Collect(ComponentAnalysis analysis, ViewPartRegistry registry, CssScopeRegistry cssScopes)
    {
        // Reached entries are keyed by MethodKey so this doubles as the visited set: adding an entry
        // and checking membership are the same dictionary operation, and reinserting a registry entry
        // instance (never a copy — see the type's remarks) costs nothing extra.
        var reached = new Dictionary<string, ViewPartDefinitionEntry>(StringComparer.Ordinal);

        if (analysis.Template is not null)
        {
            var pending = new Stack<RenderNode>();
            pending.Push(analysis.Template);

            while (pending.Count > 0)
            {
                var node = pending.Pop();

                if (node is ViewPartCallNode call && !reached.ContainsKey(call.MethodKey))
                {
                    // An unresolved key is not added: Expand's own registry.TryGet on this same key will
                    // fail identically against the subset, since the subset never claims to contain a key
                    // the full registry does not have either.
                    if (registry.TryGet(call.MethodKey, out var entry))
                    {
                        reached[call.MethodKey] = entry;

                        // A definition-less entry (declared but with no valid body) still belongs in the
                        // subset: Expand distinguishes "TryGet failed" (BCF1002 at the call site) from
                        // "TryGet succeeded, Definition is null" (already diagnosed at the declaration).
                        // Omitting the entry here would turn the second case into the first downstream.
                        if (entry.Definition is not null)
                            pending.Push(entry.Definition.Body);
                    }
                }

                foreach (var child in KeyabilityResolver.Children(node))
                    pending.Push(child);
            }
        }

        var viewParts = reached.Count == 0
            ? []
            : reached.Values
                .OrderBy(static e => e.MethodKey, StringComparer.Ordinal)
                .ToImmutableArray();

        var cssFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { analysis.FilePath };
        foreach (var entry in viewParts)
            cssFilePaths.Add(entry.FilePath);

        var cssEntriesBuilder = ImmutableArray.CreateBuilder<CssScopeEntry>(cssFilePaths.Count);
        foreach (var path in cssFilePaths)
        {
            if (cssScopes.TryGetEntryForComponentFile(path, out var cssEntry))
                cssEntriesBuilder.Add(cssEntry);
        }

        var cssEntries = cssEntriesBuilder.Count == 0
            ? []
            : cssEntriesBuilder
                .OrderBy(static e => e.CssFilePath, StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();

        return (viewParts, cssEntries);
    }
}

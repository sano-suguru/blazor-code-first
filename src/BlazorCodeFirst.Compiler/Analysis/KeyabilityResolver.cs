using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Diagnostics;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>The frame kind a template's root produces, deciding whether a ForEach key can attach to it.</summary>
internal enum ContentRootKind
{
    /// <summary>A single element/component frame; a <c>SetKey</c> can attach here.</summary>
    Element,

    /// <summary>A region frame (bare If/ForEach, or a view part whose body is region-rooted); no keyable frame.</summary>
    Region,

    /// <summary>A view part call whose target cannot be resolved (metadata-only, invalid, or cyclic).</summary>
    Unresolved,
}

/// <summary>
/// What a template's root resolves to: the frame kind, and whether that frame already carries a key of
/// its own.
/// </summary>
/// <remarks>
/// Two answers from one walk rather than two walks. They are separate questions — one about the frame a
/// node opens, one about a decoration written on it — but reaching the root is the same traversal, and a
/// second copy of it would be free to disagree about which node the root is. That is the disagreement
/// BCF3003 and BCF3032 cannot afford, since between them they decide whether a <c>SetKey</c> has a frame
/// to land on and whether it would be the second one landing there.
/// </remarks>
/// <param name="Kind">The frame kind the template's root produces.</param>
/// <param name="IsKeyed">
/// Whether the root carries a <c>.Key</c>. Always <see langword="false"/> for a root that is not an
/// element or component, which has nowhere to carry one.
/// </param>
internal readonly record struct ContentRoot(ContentRootKind Kind, bool IsKeyed);

/// <summary>
/// Determines ForEach content keyability from the value-model templates and the view part registry, and
/// collects BCF3003 for region-rooted content and BCF3032 for content already keyed at its root. This is reachability-independent (it walks templates, not
/// expansions) and registry-driven for view-part-call content, so BCF3003 fires once per definition/
/// component regardless of call sites, replacing the former per-expansion emission.
/// </summary>
internal static class KeyabilityResolver
{
    /// <summary>
    /// Resolves the root of <paramref name="node"/>, following view part calls and expanded view part
    /// bodies transitively: the frame kind and whether that frame is already keyed. Used both by the
    /// reachability-independent template walk (<see cref="CollectForEachContentDiagnostics"/>, where
    /// <paramref name="node"/> may still hold <see cref="ContentHoleNode"/>/<see cref="ViewPartCallNode"/>)
    /// and by <see cref="Generation.ViewPartExpander"/>'s post-expansion re-check (where it never does,
    /// having already been replaced or inlined — this function's <see cref="ExpansionNode"/> case plays the
    /// same "keep going into the call's body" role <see cref="ResolveCall"/> plays before expansion).
    /// </summary>
    public static ContentRoot ResolveRoot(RenderNode node, ViewPartRegistry registry) =>
        ResolveRoot(node, registry, new HashSet<string>(System.StringComparer.Ordinal), content: null);

    /// <param name="node">The node whose root is being resolved.</param>
    /// <param name="registry">The view part registry, followed transitively when the root is a view part call.</param>
    /// <param name="activeKeys">The method keys already on the call stack, guarding against a cyclic view part call.</param>
    /// <param name="content">
    /// The content the enclosing call supplied, by callee ordinal, or <see langword="null"/> when this walk has
    /// no call above it — which is the registry pass over a definition nobody calls.
    /// </param>
    private static ContentRoot ResolveRoot(
        RenderNode node,
        ViewPartRegistry registry,
        HashSet<string> activeKeys,
        IReadOnlyDictionary<int, RenderNode>? content) =>
        node switch
        {
            ComponentNode component =>
                new ContentRoot(ContentRootKind.Element, component.Key is not null),
            ElementNode element =>
                new ContentRoot(ContentRootKind.Element, element.Key is not null),
            IfNode or ForEachNode or TextContentNode
                or FragmentNode or RawMarkupNode
                or RenderFragmentContentNode
                or OpaqueViewNode or TransplantedIfNode
                    => new ContentRoot(ContentRootKind.Region, IsKeyed: false),
            TransplantedBlockNode transplanted =>
                ResolveRoot(transplanted.Content, registry, activeKeys, content),
            ExpansionNode expansion =>
                ResolveRoot(expansion.Body, registry, activeKeys, content),
            ContentHoleNode hole => ResolveHole(hole, registry, activeKeys, content),
            ViewPartCallNode call => ResolveCall(call, registry, activeKeys),
            _ => throw new System.NotSupportedException(
                $"Unknown RenderNode type '{node.GetType().Name}'; add a ResolveRoot case for it."),
        };

    /// <summary>
    /// A content hole's root is whatever the caller put there, so it is resolved against the enclosing call's
    /// own content when there is one.
    /// </summary>
    /// <remarks>
    /// Without this, <c>ForEach(xs, k, x =&gt; Bare()[Li[x]])</c> — where <c>Bare()</c> is <c>Slot</c> — reports
    /// BCF3003 although the caller supplied a keyable <c>Li</c> at that very site, and the author cannot act on
    /// it: the suggested container would have to be added inside someone else's <c>[ViewPart]</c>.
    /// <para>
    /// <see cref="ContentRootKind.Region"/> stays the answer when there is no call above the walk. That is the
    /// registry pass over an uncalled definition, where no caller exists to ask and the conservative answer is
    /// the only one available — the case the reachability-independence of this resolver is about.
    /// </para>
    /// </remarks>
    private static ContentRoot ResolveHole(
        ContentHoleNode hole,
        ViewPartRegistry registry,
        HashSet<string> activeKeys,
        IReadOnlyDictionary<int, RenderNode>? content) =>
        content is not null && content.TryGetValue(hole.ParameterOrdinal, out var supplied)
            ? ResolveRoot(supplied, registry, activeKeys, content: null)
            : new ContentRoot(ContentRootKind.Region, IsKeyed: false);

    private static ContentRoot ResolveCall(
        ViewPartCallNode call,
        ViewPartRegistry registry,
        HashSet<string> activeKeys)
    {
        // A cycle cannot be resolved to a concrete root; treat as unresolved and let expansion's BCF1002
        // (call-dependent) report the cycle.
        if (!activeKeys.Add(call.MethodKey))
            return new ContentRoot(ContentRootKind.Unresolved, IsKeyed: false);

        try
        {
            if (!registry.TryGet(call.MethodKey, out var entry) || entry.Definition is null)
                return new ContentRoot(ContentRootKind.Unresolved, IsKeyed: false);

            // The callee's body is resolved with this call's content in hand, so a hole in it answers with what
            // was actually passed. The supplied subtree is then resolved with no content of its own: it was
            // written in the caller's scope, where this call's ordinals mean nothing.
            IReadOnlyDictionary<int, RenderNode>? content = null;
            if (call.ContentArguments.Length > 0)
            {
                var byOrdinal = new Dictionary<int, RenderNode>(call.ContentArguments.Length);
                foreach (var contentArgument in call.ContentArguments.AsImmutableArray())
                    byOrdinal[contentArgument.ParameterOrdinal] = contentArgument.Content;
                content = byOrdinal;
            }

            return ResolveRoot(entry.Definition.Body, registry, activeKeys, content);
        }
        finally
        {
            activeKeys.Remove(call.MethodKey);
        }
    }

    /// <summary>
    /// Enumerates <paramref name="node"/>'s immediate child nodes, phase-agnostically. The one exhaustive
    /// child walk every traversal over this hierarchy goes through — <see cref="CollectForEachContentDiagnostics"/>
    /// is currently its only caller.
    /// </summary>
    public static IEnumerable<RenderNode> Children(RenderNode node) => node switch
    {
        IfNode n => n.Otherwise is null ? [n.Then] : [n.Then, n.Otherwise],
        TransplantedIfNode n => n.Otherwise is null ? [n.Then] : [n.Then, n.Otherwise],
        ExpansionNode n => [n.Body],
        ForEachNode n => [n.Content],
        ComponentNode n => n.Slots.AsImmutableArray().Select(static s => s.Content),
        ElementNode n => n.Children.AsImmutableArray(),
        TextContentNode => [],
        FragmentNode n => n.Children.AsImmutableArray(),
        RawMarkupNode => [],
        RenderFragmentContentNode => [],
        OpaqueViewNode => [],
        TransplantedBlockNode n => [n.Content],
        ContentHoleNode => [],
        ViewPartCallNode n => n.ContentArguments.AsImmutableArray().Select(static a => a.Content),
        _ => throw new System.NotSupportedException(
            $"Unknown RenderNode type '{node.GetType().Name}'; add a Children case for it."),
    };

    /// <summary>
    /// Walks <paramref name="node"/> and appends, for every <em>keyed</em> ForEach, a BCF3003 when its
    /// content root resolves to <see cref="ContentRootKind.Region"/> and a BCF3032 when that root already
    /// carries a key of its own. Unresolved content is skipped (BCF1002 covers it at expansion), and a
    /// ForEach whose key was declined is skipped because it attaches no key at all (#172).
    /// </summary>
    public static void CollectForEachContentDiagnostics(
        RenderNode node,
        ViewPartRegistry registry,
        ImmutableArray<DiagnosticInfo>.Builder sink)
    {
        if (node is ForEachNode { Key: not null } forEach)
        {
            var root = ResolveRoot(forEach.Content, registry);

            // Exclusive by construction, not by ordering: a region root has nowhere to write a
            // .Key, so IsKeyed cannot hold where Kind is Region.
            if (root.Kind == ContentRootKind.Region)
            {
                sink.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3003,
                    // Populated by construction; CollectForEachContentDiagnostics only ever walks a
                    // pre-expansion tree (registry pass over a ViewPartDefinition.Body, or a component's
                    // own un-expanded Body), where Location is never cleared.
                    forEach.Location!.Value.ToLocation(),
                    []));
            }
            else if (root.IsKeyed)
            {
                sink.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3032,
                    forEach.Location!.Value.ToLocation(),
                    []));
            }
        }

        // Only a keyed loop asks anything of its content root (checked above); the walk into the content
        // continues either way, since a keyed ForEach nested inside declined content is still keyed.
        foreach (var child in Children(node))
            CollectForEachContentDiagnostics(child, registry, sink);
    }

    /// <summary>
    /// Collects BCF3003 for every valid view part definition's body, reachability-independent (covers
    /// view parts that are never called). Deduped per definition by walking each body once.
    /// </summary>
    public static ImmutableArray<DiagnosticInfo> CollectViewPartForEachDiagnostics(ViewPartRegistry registry)
    {
        var sink = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var entry in registry.Entries)
        {
            if (entry.Definition is not null)
                CollectForEachContentDiagnostics(entry.Definition.Body, registry, sink);
        }

        return sink.ToImmutable();
    }
}

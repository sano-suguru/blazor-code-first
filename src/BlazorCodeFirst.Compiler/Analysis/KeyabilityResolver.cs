using System.Collections.Generic;
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
/// Determines ForEach content keyability from the value-model templates and the view part registry, and
/// collects BCF3003 for region-rooted content. This is reachability-independent (it walks templates, not
/// expansions) and registry-driven for view-part-call content, so BCF3003 fires once per definition/
/// component regardless of call sites, replacing the former per-expansion emission.
/// </summary>
internal static class KeyabilityResolver
{
    /// <summary>Resolves the root frame kind of <paramref name="node"/>, following view part calls transitively.</summary>
    public static ContentRootKind ResolveRootKind(RenderTemplateNode node, ViewPartRegistry registry) =>
        ResolveRootKind(node, registry, new HashSet<string>(System.StringComparer.Ordinal), content: null);

    /// <param name="content">
    /// The content the enclosing call supplied, by callee ordinal, or <see langword="null"/> when this walk has
    /// no call above it — which is the registry pass over a definition nobody calls.
    /// </param>
    private static ContentRootKind ResolveRootKind(
        RenderTemplateNode node,
        ViewPartRegistry registry,
        HashSet<string> activeKeys,
        IReadOnlyDictionary<int, RenderTemplateNode>? content) =>
        node switch
        {
            ComponentTemplateNode or ElementTemplateNode => ContentRootKind.Element,
            IfTemplateNode or ForEachTemplateNode or TextContentTemplateNode
                or FragmentTemplateNode or RawMarkupTemplateNode
                or RenderFragmentContentTemplateNode
                or OpaqueViewTemplateNode => ContentRootKind.Region,
            TransplantedBlockTemplateNode transplanted =>
                ResolveRootKind(transplanted.Content, registry, activeKeys, content),
            ContentHoleTemplateNode hole => ResolveHole(hole, registry, activeKeys, content),
            ViewPartCallTemplateNode call => ResolveCall(call, registry, activeKeys),
            _ => throw new System.NotSupportedException(
                $"Unknown RenderTemplateNode type '{node.GetType().Name}'; add a ResolveRootKind case for it."),
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
    private static ContentRootKind ResolveHole(
        ContentHoleTemplateNode hole,
        ViewPartRegistry registry,
        HashSet<string> activeKeys,
        IReadOnlyDictionary<int, RenderTemplateNode>? content) =>
        content is not null && content.TryGetValue(hole.ParameterOrdinal, out var supplied)
            ? ResolveRootKind(supplied, registry, activeKeys, content: null)
            : ContentRootKind.Region;

    private static ContentRootKind ResolveCall(
        ViewPartCallTemplateNode call,
        ViewPartRegistry registry,
        HashSet<string> activeKeys)
    {
        // A cycle cannot be resolved to a concrete root; treat as unresolved and let expansion's BCF1002
        // (call-dependent) report the cycle.
        if (!activeKeys.Add(call.MethodKey))
            return ContentRootKind.Unresolved;

        try
        {
            if (!registry.TryGet(call.MethodKey, out var entry) || entry.Definition is null)
                return ContentRootKind.Unresolved;

            // The callee's body is resolved with this call's content in hand, so a hole in it answers with what
            // was actually passed. The supplied subtree is then resolved with no content of its own: it was
            // written in the caller's scope, where this call's ordinals mean nothing.
            IReadOnlyDictionary<int, RenderTemplateNode>? content = null;
            if (call.ContentArguments.Length > 0)
            {
                var byOrdinal = new Dictionary<int, RenderTemplateNode>(call.ContentArguments.Length);
                foreach (var contentArgument in call.ContentArguments.AsImmutableArray())
                    byOrdinal[contentArgument.ParameterOrdinal] = contentArgument.Content;
                content = byOrdinal;
            }

            return ResolveRootKind(entry.Definition.Body, registry, activeKeys, content);
        }
        finally
        {
            activeKeys.Remove(call.MethodKey);
        }
    }

    /// <summary>
    /// Walks <paramref name="node"/> and appends a BCF3003 for every <em>keyed</em> ForEach whose content
    /// root resolves to <see cref="ContentRootKind.Region"/>. Unresolved content is skipped (BCF1002
    /// covers it at expansion), and a ForEach whose key was declined is skipped because it attaches no
    /// key at all (#172).
    /// </summary>
    public static void CollectForEachContentDiagnostics(
        RenderTemplateNode node,
        ViewPartRegistry registry,
        ImmutableArray<DiagnosticInfo>.Builder sink)
    {
        switch (node)
        {
            case ForEachTemplateNode forEach:
                // Only a keyed loop asks anything of its content root. A declined key emits no SetKey, so
                // a Fragment, a Raw or a bare If roots the content legitimately (#172). The walk into the
                // content continues either way: a keyed ForEach nested inside declined content is still
                // keyed.
                if (forEach.Key is not null
                    && ResolveRootKind(forEach.Content, registry) == ContentRootKind.Region)
                {
                    sink.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BCF3003,
                        forEach.Location.ToLocation(),
                        []));
                }

                CollectForEachContentDiagnostics(forEach.Content, registry, sink);
                break;

            case ElementTemplateNode element:
                foreach (var child in element.Children.AsImmutableArray())
                    CollectForEachContentDiagnostics(child, registry, sink);
                break;

            case IfTemplateNode ifNode:
                CollectForEachContentDiagnostics(ifNode.Then, registry, sink);
                if (ifNode.Otherwise is not null)
                    CollectForEachContentDiagnostics(ifNode.Otherwise, registry, sink);
                break;

            case FragmentTemplateNode fragment:
                foreach (var child in fragment.Children.AsImmutableArray())
                    CollectForEachContentDiagnostics(child, registry, sink);
                break;

            case ComponentTemplateNode component:
                foreach (var slot in component.Slots.AsImmutableArray())
                    CollectForEachContentDiagnostics(slot.Content, registry, sink);
                break;

            case TransplantedBlockTemplateNode transplanted:
                CollectForEachContentDiagnostics(transplanted.Content, registry, sink);
                break;

            case ViewPartCallTemplateNode call:
                // The call's own body is walked once from the registry pass
                // (CollectViewPartForEachDiagnostics) and deliberately not re-walked here. What is walked
                // here is the content the *call site* supplies, in brackets or as a View argument: both are
                // subtrees written at this site, so a ForEach inside one belongs to this walk and would
                // otherwise never be visited.
                foreach (var contentArgument in call.ContentArguments.AsImmutableArray())
                    CollectForEachContentDiagnostics(contentArgument.Content, registry, sink);
                break;

            // No nested template children to walk. Listed as cases rather than left to fall through, so the
            // default arm below can exist: this is the third exhaustive dispatch over the hierarchy, and the
            // other two (ResolveRootKind above, ViewPartExpander.ExpandNode) both throw on an unknown node.
            // A node type added without a case here would mean a BCF3003 that never fires, which is invisible.
            case TextContentTemplateNode:
            case ContentHoleTemplateNode:
            case RawMarkupTemplateNode:
            case RenderFragmentContentTemplateNode:
            case OpaqueViewTemplateNode:
                break;

            default:
                throw new System.NotSupportedException(
                    $"Unknown RenderTemplateNode type '{node.GetType().Name}'; add a "
                        + $"{nameof(CollectForEachContentDiagnostics)} case for it.");
        }
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

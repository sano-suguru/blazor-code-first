using System.Collections.Generic;
using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Diagnostics;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>The frame kind a template's root produces, deciding whether a ForEach key can attach to it.</summary>
internal enum ContentRootKind
{
    /// <summary>A single element/component frame; a <c>SetKey</c> can attach here.</summary>
    Element,

    /// <summary>A region frame (bare If/ForEach, or a composable whose body is region-rooted); no keyable frame.</summary>
    Region,

    /// <summary>A composable call whose target cannot be resolved (metadata-only, invalid, or cyclic).</summary>
    Unresolved,
}

/// <summary>
/// Determines ForEach content keyability from the value-model templates and the composable registry, and
/// collects BCF3003 for region-rooted content. This is reachability-independent (it walks templates, not
/// expansions) and registry-driven for composable-call content, so BCF3003 fires once per definition/
/// component regardless of call sites, replacing the former per-expansion emission.
/// </summary>
internal static class KeyabilityResolver
{
    /// <summary>Resolves the root frame kind of <paramref name="node"/>, following composable calls transitively.</summary>
    public static ContentRootKind ResolveRootKind(RenderTemplateNode node, ComposableRegistry registry) =>
        ResolveRootKind(node, registry, new HashSet<string>(System.StringComparer.Ordinal));

    private static ContentRootKind ResolveRootKind(
        RenderTemplateNode node,
        ComposableRegistry registry,
        HashSet<string> activeKeys) =>
        node switch
        {
            ComponentTemplateNode or ElementTemplateNode => ContentRootKind.Element,
            IfTemplateNode or ForEachTemplateNode or TextContentTemplateNode
                or FragmentTemplateNode or RawMarkupTemplateNode
                or RenderFragmentContentTemplateNode => ContentRootKind.Region,
            // A content hole's root is whatever the caller passes, which this walk cannot see and must not
            // guess: it is reachability-independent by design, and the same definition may be called with a
            // keyable element from one site and a bare If from another. Region is the answer that holds for
            // both, so a ForEach whose content root *is* a slot or a View parameter is BCF3003 at the
            // declaration -- the existing diagnostic, with no new one and no loss of the property.
            // ForEach(items, k, x => Div[Slot]) is unaffected: Div is the root there.
            ContentHoleTemplateNode => ContentRootKind.Region,
            ComposableCallTemplateNode call => ResolveCall(call, registry, activeKeys),
            _ => throw new System.NotSupportedException(
                $"Unknown RenderTemplateNode type '{node.GetType().Name}'; add a ResolveRootKind case for it."),
        };

    private static ContentRootKind ResolveCall(
        ComposableCallTemplateNode call,
        ComposableRegistry registry,
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

            return ResolveRootKind(entry.Definition.Body, registry, activeKeys);
        }
        finally
        {
            activeKeys.Remove(call.MethodKey);
        }
    }

    /// <summary>
    /// Walks <paramref name="node"/> and appends a BCF3003 for every ForEach whose content root resolves to
    /// <see cref="ContentRootKind.Region"/>. Unresolved content is skipped (BCF1002 covers it at expansion).
    /// </summary>
    public static void CollectForEachContentDiagnostics(
        RenderTemplateNode node,
        ComposableRegistry registry,
        ImmutableArray<DiagnosticInfo>.Builder sink)
    {
        switch (node)
        {
            case ForEachTemplateNode forEach:
                if (ResolveRootKind(forEach.Content, registry) == ContentRootKind.Region)
                    sink.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BCF3003,
                        forEach.Location.ToLocation(),
                        []));
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

            case ComposableCallTemplateNode call:
                // The call's own body is walked once from the registry pass
                // (CollectComposableForEachDiagnostics) and deliberately not re-walked here. What is walked
                // here is the content the *call site* supplies, in brackets or as a View argument: those are
                // subtrees written at this site, so a ForEach inside one belongs to this walk and would
                // otherwise never be visited.
                foreach (var contentArgument in call.ContentArguments.AsImmutableArray())
                    CollectForEachContentDiagnostics(contentArgument.Content, registry, sink);
                foreach (var child in call.SlotContent.AsImmutableArray())
                    CollectForEachContentDiagnostics(child, registry, sink);
                break;

                // TextContentTemplateNode/ContentHoleTemplateNode/RawMarkupTemplateNode/
                // RenderFragmentContentTemplateNode have no nested template children to walk.
        }
    }

    /// <summary>
    /// Collects BCF3003 for every valid composable definition's body, reachability-independent (covers
    /// composables that are never called). Deduped per definition by walking each body once.
    /// </summary>
    public static ImmutableArray<DiagnosticInfo> CollectComposableForEachDiagnostics(ComposableRegistry registry)
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

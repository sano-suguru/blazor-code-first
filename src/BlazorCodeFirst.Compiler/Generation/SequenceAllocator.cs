using System;
using System.Linq;

namespace BlazorCodeFirst.Compiler.Generation;

/// <summary>
/// Computes preorder sequence widths for the SSC render-node tree.
///
/// <para>
/// <em>Width</em> is defined as the count of <c>RenderTreeBuilder</c> calls that consume a sequence
/// argument in the subtree rooted at the given node. <c>CloseElement</c> and <c>CloseRegion</c>
/// do <em>not</em> consume sequence numbers and are therefore excluded from the width calculation.
/// </para>
/// </summary>
internal static class SequenceAllocator
{
    /// <summary>
    /// Returns the number of sequence-consuming <c>RenderTreeBuilder</c> calls produced by
    /// <paramref name="node"/> and its entire subtree.
    /// </summary>
    public static int Width(RenderNode node) => node switch
    {
        // OpenRegion(k) = 1 call, then both branches (disjoint ranges: then=[k+1, k+1+W(T1)),
        // else=[k+1+W(T1), k+1+W(T1)+W(T2))). Both widths are reserved regardless of which
        // branch executes so that the sequence of any following sibling is stable.
        IfNode { Then: var then, Otherwise: var otherwise } =>
            1 + Width(then) + (otherwise is null ? 0 : Width(otherwise)),

        ExpansionNode { Body: var body } => Width(body),

        // OpenRegion(k) = 1 call; SetKey/CloseRegion/foreach consume no sequence number.
        // The content template occupies one static sequence space reused each iteration.
        ForEachNode { Content: var content } => 1 + Width(content),

        // OpenComponent(k) = 1 call, plus one AddComponentParameter per scalar parameter, plus per slot
        // one AddComponentParameter for the fragment itself and the whole width of its content. The
        // lambda continues this same flat counter rather than opening a new sequence space: a slot's
        // frames belong to the *child* component's frame list, and a host that invokes the fragment
        // directly (instead of AddContent) gets no isolating region, so restarting at 0 collides with the
        // host's own low numbers and remounts components. SetKey/CloseComponent consume no sequence number.
        ComponentNode { Parameters: var parameters, Slots: var slots } =>
            1 + parameters.Length + slots.Sum(static slot => 1 + Width(slot.Content)),

        // OpenElement(tag) = 1 call, +1 if class-decorated, +1 per attribute, +1 per event, plus the sum
        // of all children. Must branch on the identical conditions and order as
        // RenderViewEmitter.EmitElement (class fold -> attributes -> events -> children).
        ElementNode { Classes: var classes, Attributes: var attributes, Events: var events, Children: var children } =>
            1 + (classes.Length == 0 ? 0 : 1) + attributes.Length + events.Length + children.Sum(Width),

        // AddContent = 1 call; no wrapping element.
        TextContentNode => 1,

        // Fragment: no wrapping element; width is the sum of children (like ExpansionNode without locals).
        FragmentNode { Children: var children } => children.Sum(Width),

        // AddMarkupContent = 1 call; no wrapping element (mirrors TextContentNode's AddContent = 1).
        RawMarkupNode => 1,

        // AddContent(seq, RenderFragment?) = 1 sequence-consuming call. The region frame it opens is
        // emitted only for a non-null fragment, but the call is unconditional (mirrors TextContentNode).
        RenderFragmentContentNode => 1,

        _ => throw new NotSupportedException(
            $"Unknown RenderNode type '{node.GetType().Name}'; add a Width case for it."),
    };
}

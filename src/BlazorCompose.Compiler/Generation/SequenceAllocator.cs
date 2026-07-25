using System;
using System.Linq;

namespace BlazorCompose.Compiler.Generation;

/// <summary>
/// Computes preorder sequence widths for the SSC render-node tree.
///
/// <para>
/// <em>Width</em> is defined as the count of <c>RenderTreeBuilder</c> calls that consume a sequence
/// argument in the subtree rooted at the given node.  <c>CloseElement</c> and <c>CloseRegion</c>
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
        // else=[k+1+W(T1), k+1+W(T1)+W(T2))).  Both widths are reserved regardless of which
        // branch executes so that the sequence of any following sibling is stable.
        IfNode { Then: var then, Otherwise: var otherwise } =>
            1 + Width(then) + (otherwise is null ? 0 : Width(otherwise)),

        ExpansionNode { Body: var body } => Width(body),

        // OpenRegion(k) = 1 call; SetKey/CloseRegion/foreach consume no sequence number.
        // The content template occupies one static sequence space reused each iteration.
        ForEachNode { Content: var content } => 1 + Width(content),

        // OpenComponent(k) = 1 call, plus one AddComponentParameter per parameter.
        // SetKey/CloseComponent consume no sequence number.
        ComponentNode { Parameters: var parameters } => 1 + parameters.Length,

        // OpenElement(tag) = 1 call, +1 if class-decorated, +1 per attribute, +1 per event, plus the sum
        // of all children. Must branch on the identical conditions and order as
        // RenderBodyEmitter.EmitElement (class fold -> attributes -> events -> children).
        ElementNode { Classes: var classes, Attributes: var attributes, Events: var events, Children: var children } =>
            1 + (classes.Length == 0 ? 0 : 1) + attributes.Length + events.Length + children.Sum(Width),

        // AddContent = 1 call; no wrapping element.
        TextContentNode => 1,

        _ => throw new NotSupportedException(
            $"Unknown RenderNode type '{node.GetType().Name}'; add a Width case for it."),
    };
}

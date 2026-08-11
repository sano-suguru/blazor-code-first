namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Which of the design-time surface's <c>params ReadOnlySpan&lt;View&gt;</c> child lists a bracketed
/// expression indexes, as answered by <see cref="KnownSymbols.ClassifyChildrenIndexer"/>.
/// </summary>
/// <remarks>
/// The three receivers take children through three distinct indexer symbols and mean three different
/// things downstream — an element's children, a component's <c>ChildContent</c>, and the content a
/// <c>[ViewPart]</c> declared with <c>SlotView</c> places at its <c>Slot</c>. A kind rather than three
/// booleans so a caller that recognizes only some of them says which, in a shape a reader can check
/// against this list.
/// </remarks>
internal enum ChildrenIndexerKind
{
    /// <summary>Not one of the three, including an ordinary indexer such as <c>_dict["k"]</c>.</summary>
    None,

    /// <summary><c>ElementView</c>'s indexer: an element's children.</summary>
    Element,

    /// <summary><c>ComponentView&lt;T&gt;</c>'s indexer: a component's <c>ChildContent</c>.</summary>
    Component,

    /// <summary><c>SlotView</c>'s indexer: the content a content-taking <c>[ViewPart]</c> is given.</summary>
    Content,
}

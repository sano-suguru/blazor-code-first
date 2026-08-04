using System.Collections.Immutable;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The emitter's static fold (ARCHITECTURE.md §2.7(D)). Nodes are built directly rather than compiled
/// from source, so each case states exactly which expressions are constant.
/// </summary>
public sealed class RenderViewEmitterFoldTests
{
    private static string EmitRoot(RenderNode root) =>
        RenderViewEmitter.Emit(new ComponentModel(
            HintName: "T.g.cs", ClassName: "T", TypeParameters: default, Namespace: null,
            RootNode: root)).ToString();

    private static ExpressionTemplate Const(string value) =>
        ExpressionTemplate.Create(
            [new LiteralExpressionSegment(
                Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true))],
            new ConstantInfo(value));

    private static ExpressionTemplate Dynamic(string code) => ExpressionTemplate.Literal(code);

    private static ElementNode Element(string tag, params RenderNode[] children) =>
        new(tag, default, default, default, ImmutableArray.Create(children));

    private static ElementNode StaticSpan(string text) =>
        Element("span", new TextContentNode(Const(text)));

    /// <summary>
    /// An element that cannot fold itself, because its class expression is not constant, and so emits its
    /// children through <c>EmitChildren</c>. The run-level cases wrap their children in this: a foldable
    /// parent would absorb the whole subtree at the root and the run boundary would no longer be visible.
    /// Its own frames occupy 0 (the element) and 1 (the class attribute), so children start at 2.
    /// </summary>
    private static ElementNode DynamicHost(string tag, params RenderNode[] children) =>
        new(tag, ImmutableArray.Create(Dynamic("_cls")), default, default, ImmutableArray.Create(children));

    [Fact]
    public void AdjacentStaticSiblings_CoalesceIntoOneMarkupFrame()
    {
        var emitted = EmitRoot(DynamicHost("div", StaticSpan("a"), StaticSpan("b")));

        Assert.Contains(
            """__builder.AddMarkupContent(2, "<span>a</span><span>b</span>");""",
            emitted);
        Assert.DoesNotContain("OpenElement(2", emitted);
    }

    /// <summary>
    /// The run is the fold unit, not the subtree: a dynamic sibling between two static ones splits them
    /// into two runs rather than leaving both unfolded (the correction #142's measurement recorded).
    /// </summary>
    [Fact]
    public void ADynamicSiblingSplitsTheRunWithoutBlockingIt()
    {
        var emitted = EmitRoot(Element(
            "div",
            StaticSpan("a"),
            Element("span", new TextContentNode(Dynamic("Count"))),
            StaticSpan("b")));

        Assert.Contains("""__builder.AddMarkupContent(1, "<span>a</span>");""", emitted);
        Assert.Contains("""__builder.OpenElement(2, "span");""", emitted);
        Assert.Contains("__builder.AddContent(3, Count);", emitted);
        Assert.Contains("""__builder.AddMarkupContent(4, "<span>b</span>");""", emitted);
    }

    /// <summary>A lone text node is one frame either way, so folding it would buy nothing.</summary>
    [Fact]
    public void ALoneStaticTextChild_IsNotFolded()
    {
        var emitted = EmitRoot(DynamicHost("span", new TextContentNode(Const("a"))));

        Assert.Contains("""__builder.AddContent(2, "a");""", emitted);
        Assert.DoesNotContain("AddMarkupContent", emitted);
    }

    /// <summary>The other side of the <c>absorbed &gt;= 2</c> boundary: an element plus its text child.</summary>
    [Fact]
    public void ALoneStaticElementWithAChild_IsFolded()
    {
        var emitted = EmitRoot(DynamicHost("div", StaticSpan("a")));

        Assert.Contains("""__builder.AddMarkupContent(2, "<span>a</span>");""", emitted);
    }

    /// <summary>
    /// <c>model.RootNode</c> is a single-node fold position like an <c>If</c> branch or a slot's content
    /// (design §4.3), and nothing threads a key to it, so a fully static root becomes one markup frame.
    /// </summary>
    [Fact]
    public void AFullyStaticRoot_FoldsToOneFrame()
    {
        var emitted = EmitRoot(Element("div", StaticSpan("a")));

        Assert.Contains("""__builder.AddMarkupContent(0, "<div><span>a</span></div>");""", emitted);
        Assert.DoesNotContain("OpenElement", emitted);
    }

    [Fact]
    public void AFullyStaticFragmentRoot_FoldsToOneFrame()
    {
        var emitted = EmitRoot(new FragmentNode(
            ImmutableArray.Create<RenderNode>(StaticSpan("a"), StaticSpan("b"))));

        Assert.Contains(
            """__builder.AddMarkupContent(0, "<span>a</span><span>b</span>");""",
            emitted);
    }

    /// <summary>
    /// A markup frame cannot take SetKey, so a ForEach content root must never fold. The emitter's own
    /// key parameter is what enforces it: EmitForEach is the only caller that passes a non-null key.
    /// </summary>
    [Fact]
    public void AForEachContentRoot_IsNotFolded()
    {
        var emitted = EmitRoot(new ForEachNode(
            Dynamic("_items"),
            Dynamic("item.Id"),
            Element("div", StaticSpan("a")),
            "item"));

        Assert.Contains("""__builder.OpenElement(1, "div");""", emitted);
        Assert.Contains("__builder.SetKey(item.Id);", emitted);
        // The content root stays an element, but its own static children still fold.
        Assert.Contains("""__builder.AddMarkupContent(2, "<span>a</span>");""", emitted);
    }

    [Fact]
    public void BothIfBranches_FoldAndTheFollowingSiblingSequenceAccountsForThem()
    {
        var emitted = EmitRoot(Element(
            "div",
            new IfNode(Dynamic("_flag"), Element("p", StaticSpan("t")), Element("p", StaticSpan("f"))),
            StaticSpan("after")));

        Assert.Contains("__builder.OpenRegion(1);", emitted);
        Assert.Contains("""__builder.AddMarkupContent(2, "<p><span>t</span></p>");""", emitted);
        Assert.Contains("""__builder.AddMarkupContent(3, "<p><span>f</span></p>");""", emitted);
        Assert.Contains("""__builder.AddMarkupContent(4, "<span>after</span>");""", emitted);
    }

    [Fact]
    public void AComponentSlotContent_IsFolded()
    {
        var emitted = EmitRoot(new ComponentNode(
            "global::T.Card",
            default,
            ImmutableArray.Create(new ComponentSlotNode("ChildContent", Element("p", StaticSpan("a"))))));

        Assert.Contains("""__builder.AddMarkupContent(2, "<p><span>a</span></p>");""", emitted);
    }

    [Fact]
    public void ARawMarkupSibling_SplitsTheRun()
    {
        var emitted = EmitRoot(Element(
            "div",
            StaticSpan("a"),
            new RawMarkupNode(Const("<i>x</i>")),
            StaticSpan("b")));

        Assert.Contains("""__builder.AddMarkupContent(1, "<span>a</span>");""", emitted);
        Assert.Contains("""__builder.AddMarkupContent(2, "<i>x</i>");""", emitted);
        Assert.Contains("""__builder.AddMarkupContent(3, "<span>b</span>");""", emitted);
    }

    /// <summary>
    /// A composable pass-through keeps its constant through expansion, so the expanded body folds. The
    /// locals are still declared here: dropping them is a separate rule that needs every initializer to
    /// be constant.
    /// </summary>
    [Fact]
    public void AnExpansionBodyOfConstants_IsFolded()
    {
        var emitted = EmitRoot(new ExpansionNode(
            ImmutableArray.Create(new LocalBinding("string", "__l0_0", Dynamic("Compute()"))),
            Element("div", StaticSpan("a"), StaticSpan("b"))));

        Assert.Contains("string __l0_0 = Compute();", emitted);
        // The non-constant local only keeps the ExpansionNode itself from folding as one unit (its
        // declaration cannot sit inside markup); the body underneath is emitted through the same EmitNode
        // recursion as an If branch or slot content, so a fully static body folds on its own terms,
        // sequence numbering starting fresh since the declaration consumes no sequence number.
        Assert.Contains(
            """__builder.AddMarkupContent(0, "<div><span>a</span><span>b</span></div>");""",
            emitted);
    }
}

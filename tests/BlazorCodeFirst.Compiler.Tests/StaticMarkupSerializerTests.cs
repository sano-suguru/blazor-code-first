using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Analysis;
using BlazorCodeFirst.Compiler.Generation;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Unit tests for the fold predicate and the HTML serializer. Escaping is the highest-risk part of #140,
/// because a defect there opens an XSS-class hole rather than costing time, so it is checked here
/// directly rather than through the emitter's generated text.
/// </summary>
public sealed class StaticMarkupSerializerTests
{
    /// <summary>An expression template that carries a constant string value, as analysis would produce.</summary>
    private static ExpressionTemplate Const(string value) =>
        ExpressionTemplate.Create(
            [new LiteralExpressionSegment(Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true))],
            new ConstantInfo(value));

    /// <summary>A constant of no usable string value: a constant null string, or a non-string constant.</summary>
    private static ExpressionTemplate ConstWithoutText() =>
        ExpressionTemplate.Create(
            [new LiteralExpressionSegment("null")],
            new ConstantInfo(null));

    /// <summary>A non-constant expression, as a property reference would produce.</summary>
    private static ExpressionTemplate Dynamic(string code) => ExpressionTemplate.Literal(code);

    /// <summary>
    /// A tag and children only. Cases that need classes, attributes or events construct
    /// <see cref="ElementNode"/> directly, so this helper stays a plain params list: a named argument
    /// cannot supply a single element to a <c>params</c> parameter.
    /// </summary>
    private static ElementNode Element(string tag, params RenderNode[] children) =>
        new(tag, default, default, default, ImmutableArray.Create(children));

    // --- foldable -----------------------------------------------------------------------------

    [Fact]
    public void ConstantTextContent_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(new TextContentNode(Const("a"))));

    /// <summary>
    /// A valid surrogate pair (an astral-plane character, here U+1F600) must round-trip and therefore
    /// fold. This is the positive counterpart to <see cref="TextWithLoneSurrogate_IsNotFoldable"/>: a
    /// <see cref="StaticMarkupSerializer"/> that rejected every surrogate rather than only an unpaired
    /// one would pass every other test in this file while silently disabling folding for any static
    /// text containing an emoji or other non-BMP character.
    /// </summary>
    [Fact]
    public void TextWithAstralCharacter_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(new TextContentNode(Const("a\U0001F600b"))));

    [Fact]
    public void CuratedElementWithConstantChildren_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(
            Element("h1", new TextContentNode(Const("Benchmark")))));

    [Fact]
    public void VoidElement_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(Element("br")));

    [Fact]
    public void ConstantClassAndAttribute_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "a",
            ImmutableArray.Create(Const("nav")),
            ImmutableArray.Create(new AttributeTemplate("href", Const("/home"))),
            default,
            ImmutableArray.Create<RenderNode>(new TextContentNode(Const("Home"))))));

    [Fact]
    public void ConstantNullAttributeValue_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "div",
            default,
            ImmutableArray.Create(new AttributeTemplate("id", ConstWithoutText())),
            default,
            default)));

    [Fact]
    public void CustomElementName_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(Element("my-widget")));

    [Fact]
    public void FragmentOfConstantChildren_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(new FragmentNode(
            ImmutableArray.Create<RenderNode>(
                new TextContentNode(Const("a")),
                Element("span", new TextContentNode(Const("b")))))));

    /// <summary>
    /// An expansion is foldable when every local is constant-initialized and the body is foldable: the
    /// declarations are then side-effect free and can be dropped entirely. Includes one local whose
    /// initializer is a non-string constant (<see cref="ConstWithoutText"/>, state 2 of
    /// <see cref="ConstantInfo"/>) to prove that state counts as constant too — foldability asks whether
    /// <see cref="ConstantInfo"/> is present at all, not whether it carries usable text.
    /// </summary>
    [Fact]
    public void ExpansionOfConstantLocalsWithFoldableBody_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(new ExpansionNode(
            ImmutableArray.Create(
                new LocalBinding("int", "_count", ConstWithoutText()),
                new LocalBinding("string", "_label", Const("x"))),
            Element("div"))));

    // --- not foldable -------------------------------------------------------------------------

    [Fact]
    public void DynamicTextContent_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new TextContentNode(Dynamic("Title"))));

    [Fact]
    public void TextContentWithoutStringValue_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new TextContentNode(ConstWithoutText())));

    [Fact]
    public void ElementWithAnEvent_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "button",
            default,
            default,
            ImmutableArray.Create(new EventTemplate("onclick", Dynamic("() => { }"))),
            ImmutableArray.Create<RenderNode>(new TextContentNode(Const("Save"))))));

    [Fact]
    public void ElementWithDynamicClass_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "div", ImmutableArray.Create(Dynamic("_cls")), default, default, default)));

    /// <summary>
    /// A class value requires usable text (<see cref="ConstantInfo"/> state 3): unlike an attribute
    /// value, a class with no usable text has nothing sensible to omit, so state 2
    /// (<see cref="ConstWithoutText"/>) must not fold here even though it does for an attribute value
    /// (<see cref="ConstantNullAttributeValue_IsFoldable"/>).
    /// </summary>
    [Fact]
    public void ClassWithoutStringValue_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "div", ImmutableArray.Create(ConstWithoutText()), default, default, default)));

    [Fact]
    public void ElementWithDynamicAttributeValue_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "a",
            default,
            ImmutableArray.Create(new AttributeTemplate("href", Dynamic("Url"))),
            default,
            default)));

    [Fact]
    public void ElementWithADynamicChild_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(
            Element("div", new TextContentNode(Dynamic("Title")))));

    /// <summary>
    /// One non-constant local is enough to block the fold, even though the body is foldable on its own:
    /// dropping the declarations would stop a side-effecting argument (here, the second local's
    /// initializer) from running even though the body never names it. The non-constant local is the
    /// second of two so a predicate that only inspects the first local would wrongly pass this case.
    /// </summary>
    [Fact]
    public void ExpansionWithANonConstantLocal_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ExpansionNode(
            ImmutableArray.Create(
                new LocalBinding("string", "_label", Const("x")),
                new LocalBinding("string", "_sideEffect", Dynamic("ComputeAndLog()"))),
            Element("div"))));

    /// <summary>
    /// Every local is constant, so the declarations are droppable, but the body itself is not foldable.
    /// </summary>
    [Fact]
    public void ExpansionWithConstantLocalsButNonFoldableBody_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ExpansionNode(
            ImmutableArray.Create(new LocalBinding("string", "_label", Const("x"))),
            new TextContentNode(Dynamic("Title")))));

    [Theory]
    [InlineData("script")]   // RAWTEXT, and not curated
    [InlineData("style")]    // RAWTEXT, and not curated
    [InlineData("svg")]      // foreign content, and not curated
    [InlineData("DIV")]      // curated tags are all lowercase; Element("DIV") is emitted as written
    [InlineData("marquee")]  // an uncurated ordinary tag
    public void UncuratedTag_IsNotFoldable(string tag) =>
        Assert.False(StaticMarkupSerializer.IsFoldable(Element(tag)));

    /// <summary>
    /// Curated, so the allow-list lets these through, but their HTML text interpretation differs from an
    /// ordinary element: pre and textarea lose a newline immediately after the open tag, textarea is
    /// RCDATA so a child element would become literal text, and iframe is parsed as generic raw text so
    /// character references are not resolved.
    /// </summary>
    [Theory]
    [InlineData("pre")]
    [InlineData("textarea")]
    [InlineData("iframe")]
    public void TextInterpretingTag_IsNotFoldable(string tag)
    {
        Assert.True(KnownSymbols.IsCuratedTag(tag));
        Assert.False(StaticMarkupSerializer.IsFoldable(Element(tag)));
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("bad>name")]
    [InlineData("bad=name")]
    [InlineData("bad\"name")]
    [InlineData("")]
    public void UnsafeAttributeName_IsNotFoldable(string name) =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "div",
            default,
            ImmutableArray.Create(new AttributeTemplate(name, Const("x"))),
            default,
            default)));

    /// <summary>
    /// A NUL cannot round-trip through markup: the HTML parser replaces both a literal NUL and the
    /// reference &amp;#0; with U+FFFD, while the element path's createTextNode keeps it.
    /// </summary>
    [Fact]
    public void TextWithNul_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new TextContentNode(Const("a\0b"))));

    /// <summary>
    /// A lone surrogate is refused for the same round-trip reason. Kept as its own <see cref="Fact"/>
    /// rather than an <see cref="InlineDataAttribute"/> case alongside the NUL above: VSTest marshals
    /// <c>InlineData</c> arguments through a discovery channel that mangles an unpaired surrogate into
    /// three U+FFFD before the test body runs, while a string literal built directly in the method body
    /// survives intact.
    /// </summary>
    [Fact]
    public void TextWithLoneSurrogate_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new TextContentNode(Const("a\ud800b"))));

    [Fact]
    public void RawMarkup_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new RawMarkupNode(Const("<i>x</i>"))));

    [Fact]
    public void Component_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(
            new ComponentNode("global::T.Card", default, default)));

    [Fact]
    public void If_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(
            new IfNode(Dynamic("_flag"), Element("div"), null)));

    [Fact]
    public void ForEach_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ForEachNode(
            Dynamic("_items"), Dynamic("i => i.Id"), Element("div"), "item")));

    [Fact]
    public void RenderFragmentContent_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(
            new RenderFragmentContentNode(Dynamic("ChildContent"))));
}

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

    // A name the markup path accepts and the element path does not: <div 9x="1"> parses to a 9x
    // attribute, while setAttribute("9x", …) throws InvalidCharacterError because the DOM applies the
    // XML Name production. Refused so the two paths cannot disagree.
    [InlineData("9x")]
    [InlineData("-x")]
    [InlineData(".x")]
    public void UnsafeAttributeName_IsNotFoldable(string name) =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "div",
            default,
            ImmutableArray.Create(new AttributeTemplate(name, Const("x"))),
            default,
            default)));

    /// <summary>
    /// The counterpart to <see cref="UnsafeAttributeName_IsNotFoldable"/>: the first-character rule must
    /// not be tightened past what the DOM accepts. Underscore and colon are valid XML name starts, so
    /// these still fold.
    /// </summary>
    [Theory]
    [InlineData("data-x")]
    [InlineData("_x")]
    [InlineData(":x")]
    [InlineData("x9")]
    public void SafeAttributeName_IsFoldable(string name) =>
        Assert.True(StaticMarkupSerializer.IsFoldable(new ElementNode(
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
    /// A carriage return cannot round-trip either, and unlike the NUL it is reachable from ordinary
    /// source: any verbatim string literal in a file checked out with CRLF carries one. The HTML parser
    /// normalizes CRLF and a lone CR to LF during input-stream preprocessing, so the markup path yields
    /// LF where the element path keeps CR. Measured in Chromium through the browser gate's
    /// carriage-return probe rather than derived from the specification alone.
    /// </summary>
    [Theory]
    [InlineData("a\rb")]
    [InlineData("a\r\nb")]
    [InlineData("\r")]
    public void TextWithCarriageReturn_IsNotFoldable(string value) =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new TextContentNode(Const(value))));

    /// <summary>
    /// The attribute-value case, which is the sharper one: attribute values are not whitespace-collapsed,
    /// so <c>getAttribute</c> observably returns LF on the markup path against CR on the element path.
    /// </summary>
    [Theory]
    [InlineData("a\rb")]
    [InlineData("a\r\nb")]
    public void AttributeValueWithCarriageReturn_IsNotFoldable(string value) =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "div",
            default,
            ImmutableArray.Create(new AttributeTemplate("title", Const(value))),
            default,
            default)));

    /// <summary>
    /// A class value is checked by the same round-trip rule, on its own code path.
    /// </summary>
    [Fact]
    public void ClassWithCarriageReturn_IsNotFoldable() =>
        Assert.False(StaticMarkupSerializer.IsFoldable(new ElementNode(
            "div",
            ImmutableArray.Create(Const("a\rb")),
            default,
            default,
            default)));

    /// <summary>
    /// A line feed alone is fine and must keep folding: preprocessing leaves it as it is, so both paths
    /// agree. Pins that the CR rule was not written as "reject any newline".
    /// </summary>
    [Fact]
    public void TextWithLineFeed_IsFoldable() =>
        Assert.True(StaticMarkupSerializer.IsFoldable(new TextContentNode(Const("a\nb"))));

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

    // --- serialization ------------------------------------------------------------------------

    private static (string Markup, int Absorbed) Write(params RenderNode[] run) =>
        StaticMarkupSerializer.Write(ImmutableArray.Create(run));

    [Fact]
    public void Write_TextContent_EmitsTheValue() =>
        Assert.Equal("Benchmark", Write(new TextContentNode(Const("Benchmark"))).Markup);

    [Fact]
    public void Write_Element_WrapsItsChildren() =>
        Assert.Equal(
            "<h1>Benchmark</h1>",
            Write(Element("h1", new TextContentNode(Const("Benchmark")))).Markup);

    [Fact]
    public void Write_VoidElement_HasNoClosingTag() =>
        Assert.Equal("<br>", Write(Element("br")).Markup);

    [Fact]
    public void Write_ChildlessNonVoidElement_HasAClosingTag() =>
        Assert.Equal("<div></div>", Write(Element("div")).Markup);

    [Fact]
    public void Write_SeveralSiblings_ConcatenatesThem() =>
        Assert.Equal(
            "<h1>a</h1><p>b</p>",
            Write(
                Element("h1", new TextContentNode(Const("a"))),
                Element("p", new TextContentNode(Const("b")))).Markup);

    [Fact]
    public void Write_SingleClass_EmitsOneClassAttribute() =>
        Assert.Equal(
            """<div class="card"></div>""",
            Write(new ElementNode(
                "div", ImmutableArray.Create(Const("card")), default, default, default)).Markup);

    [Fact]
    public void Write_SeveralClasses_JoinsThemWithOneSpace() =>
        Assert.Equal(
            """<div class="btn btn-primary"></div>""",
            Write(new ElementNode(
                "div",
                ImmutableArray.Create(Const("btn"), Const("btn-primary")),
                default, default, default)).Markup);

    [Fact]
    public void Write_Attribute_IsQuoted() =>
        Assert.Equal(
            """<a href="/home"></a>""",
            Write(new ElementNode(
                "a",
                default,
                ImmutableArray.Create(new AttributeTemplate("href", Const("/home"))),
                default, default)).Markup);

    /// <summary>AddAttribute omits an attribute whose value is a null string; the markup must match.</summary>
    [Fact]
    public void Write_ConstantNullAttribute_IsOmitted() =>
        Assert.Equal(
            "<div></div>",
            Write(new ElementNode(
                "div",
                default,
                ImmutableArray.Create(new AttributeTemplate("id", ConstWithoutText())),
                default, default)).Markup);

    [Fact]
    public void Write_Fragment_HasNoWrapper() =>
        Assert.Equal(
            "ab",
            Write(new FragmentNode(ImmutableArray.Create<RenderNode>(
                new TextContentNode(Const("a")),
                new TextContentNode(Const("b"))))).Markup);

    // --- escaping -----------------------------------------------------------------------------

    [Theory]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("a < b", "a &lt; b")]
    [InlineData("a > b", "a &gt; b")]
    [InlineData("</script>", "&lt;/script&gt;")]
    [InlineData("&amp;", "&amp;amp;")]
    [InlineData("<!-- x -->", "&lt;!-- x --&gt;")]
    public void Write_EscapesText(string value, string expected) =>
        Assert.Equal(expected, Write(new TextContentNode(Const(value))).Markup);

    [Theory]
    [InlineData("a & b", "a &amp; b")]
    [InlineData("a \" b", "a &quot; b")]
    [InlineData("a < b", "a &lt; b")]
    [InlineData("a > b", "a &gt; b")]
    [InlineData("\" onmouseover=\"alert(1)", "&quot; onmouseover=&quot;alert(1)")]
    public void Write_EscapesAttributeValues(string value, string expected) =>
        Assert.Equal(
            $"""<div id="{expected}"></div>""",
            Write(new ElementNode(
                "div",
                default,
                ImmutableArray.Create(new AttributeTemplate("id", Const(value))),
                default, default)).Markup);

    [Fact]
    public void Write_EscapesClassValues() =>
        Assert.Equal(
            """<div class="a&quot;b"></div>""",
            Write(new ElementNode(
                "div", ImmutableArray.Create(Const("a\"b")), default, default, default)).Markup);

    /// <summary>Text is not escaped as if it were an attribute value: a quote needs no reference there.</summary>
    [Fact]
    public void Write_LeavesQuotesAloneInText()
    {
        const string value = "say \"hi\"";

        Assert.Equal(value, Write(new TextContentNode(Const(value))).Markup);
    }

    // --- absorbed frame count -----------------------------------------------------------------

    [Fact]
    public void Write_CountsOneFrameForALoneTextNode() =>
        Assert.Equal(1, Write(new TextContentNode(Const("a"))).Absorbed);

    [Fact]
    public void Write_CountsOneFrameForAChildlessBareElement() =>
        Assert.Equal(1, Write(Element("div")).Absorbed);

    [Fact]
    public void Write_CountsOpenPlusTextForAnElementWithAChild() =>
        Assert.Equal(2, Write(Element("h1", new TextContentNode(Const("a")))).Absorbed);

    [Fact]
    public void Write_CountsOneFrameForTheWholeClassChannel() =>
        Assert.Equal(2, Write(new ElementNode(
            "div",
            ImmutableArray.Create(Const("a"), Const("b"), Const("c")),
            default, default, default)).Absorbed);

    [Fact]
    public void Write_CountsEachAttributeSeparately() =>
        Assert.Equal(3, Write(new ElementNode(
            "div",
            default,
            ImmutableArray.Create(
                new AttributeTemplate("id", Const("x")),
                new AttributeTemplate("data-a", Const("y"))),
            default, default)).Absorbed);

    /// <summary>An omitted null attribute emits no frame on the element path either, so it is not counted.</summary>
    [Fact]
    public void Write_DoesNotCountAnOmittedNullAttribute() =>
        Assert.Equal(1, Write(new ElementNode(
            "div",
            default,
            ImmutableArray.Create(new AttributeTemplate("id", ConstWithoutText())),
            default, default)).Absorbed);

    [Fact]
    public void Write_SumsAcrossARun() =>
        Assert.Equal(4, Write(
            Element("h1", new TextContentNode(Const("a"))),
            Element("p", new TextContentNode(Const("b")))).Absorbed);

    /// <summary>A fragment opens no frame of its own, so it contributes only its children.</summary>
    [Fact]
    public void Write_CountsNoFrameForAFragmentItself() =>
        Assert.Equal(2, Write(new FragmentNode(ImmutableArray.Create<RenderNode>(
            new TextContentNode(Const("a")),
            new TextContentNode(Const("b"))))).Absorbed);

    [Fact]
    public void Write_ThrowsOnANodeThatIsNotFoldable() =>
        Assert.Throws<System.NotSupportedException>(
            () => Write(new TextContentNode(Dynamic("Title"))));
}

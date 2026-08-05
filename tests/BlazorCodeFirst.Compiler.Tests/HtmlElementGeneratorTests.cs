namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlElementGeneratorTests
{
    private const string DivTextSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div["hello"];
        }
        """;

    private const string MixedContentSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private int _n = 0;
            protected override View Body =>
                Html.Div["Count: ", Html.Span["x"], "tail"];
        }
        """;

    [Fact]
    public void Div_WithBareText_EmitsDivWithAddContentNoSpan()
    {
        var result = CompilationTestHost.RunGenerator(DivTextSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Static throughout, so it folds into one markup frame — which states the claim more directly than
        // the frame pair it replaced: bare text stays bare text, with no span wrapped around it.
        Assert.Contains("__builder.AddMarkupContent(0, \"<div>hello</div>\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Div_WithMixedContent_EmitsTextAndElementChildrenInPreorder()
    {
        var result = CompilationTestHost.RunGenerator(MixedContentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Static throughout, so the whole body folds into one markup frame. Preorder is the claim and the
        // markup string carries it: text, then the element, then text, in source order.
        Assert.Contains(
            "__builder.AddMarkupContent(0, \"<div>Count: <span>x</span>tail</div>\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

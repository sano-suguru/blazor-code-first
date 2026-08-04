namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlDecorationGeneratorTests
{
    private const string ButtonOnClickSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private int _n = 0;
            protected override View Body => Html.Button.OnClick(() => _n++)["OK"];
        }
        """;

    // Non-constant class, so the div is not folded away: what this test checks is that the decoration lands
    // on its own attribute frame at seq+1.
    private const string ClassOnDivSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _cls => "panel";
            protected override View Body => Html.Div.Class(_cls)[Html.Span["x"]];
        }
        """;

    [Fact]
    public void Button_WithOnClick_EmitsOnclickAttributeThenContent()
    {
        var result = CompilationTestHost.RunGenerator(ButtonOnClickSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"button\")", generated);
        Assert.Contains(
            "__builder.AddAttribute(1, \"onclick\", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _n++))",
            generated);
        Assert.Contains("__builder.AddContent(2, \"OK\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Class_OnDiv_EmitsClassAttributeAtSeqPlusOne()
    {
        var result = CompilationTestHost.RunGenerator(ClassOnDivSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", _cls)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

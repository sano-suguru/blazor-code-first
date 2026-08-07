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

    // The generated RenderView carries no using directives, so a transplanted lambda parameter annotation
    // has to be rewritten to a global::-qualified name. That is the assumption typed event arguments rest
    // on, and AssertOutputCompiles is what proves it: the generated file is compiled without the using
    // that this input source has.
    private const string TypedOnInputSource = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;

        public partial class C : BodyComponentBase
        {
            private object? _seen;
            protected override View Body =>
                Html.Input.On("oninput", (ChangeEventArgs e) => _seen = e.Value);
        }
        """;

    private const string TypedAsyncOnInputSource = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;

        public partial class C : BodyComponentBase
        {
            private System.Threading.Tasks.Task SaveAsync(object? value) =>
                System.Threading.Tasks.Task.CompletedTask;

            protected override View Body =>
                Html.Input.On("oninput", (ChangeEventArgs e) => SaveAsync(e.Value));
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

    [Fact]
    public void On_WithTypedActionHandler_QualifiesTheLambdaParameterType()
    {
        var result = CompilationTestHost.RunGenerator(TypedOnInputSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"input\")", generated);
        Assert.Contains(
            "__builder.AddAttribute(1, \"oninput\", " +
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, " +
            "(global::Microsoft.AspNetCore.Components.ChangeEventArgs e) => _seen = e.Value))",
            generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void On_WithTypedAsyncHandler_QualifiesTheLambdaParameterType()
    {
        var result = CompilationTestHost.RunGenerator(TypedAsyncOnInputSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains(
            "__builder.AddAttribute(1, \"oninput\", " +
            "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, " +
            "(global::Microsoft.AspNetCore.Components.ChangeEventArgs e) => SaveAsync(e.Value)))",
            generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

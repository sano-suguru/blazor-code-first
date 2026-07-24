using System;

namespace BlazorCompose.Compiler.Tests;

public sealed class HtmlDecorationGeneratorTests
{
    private const string ButtonOnClickSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            private int _n = 0;
            protected override View Body => Html.Button("OK").OnClick(() => _n++);
        }
        """;

    private const string ClassOnDivSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            protected override View Body => Html.Div(Html.Span("x")).Class("panel");
        }
        """;

    private const string ClassOnIfSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            private bool _b = true;
            protected override View Body =>
                Html.If(_b, () => Html.Span("y")).Class("bad");
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
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"panel\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Class_OnIf_ReportsBC3008()
    {
        var result = CompilationTestHost.RunGenerator(ClassOnIfSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BC3008");
    }
}

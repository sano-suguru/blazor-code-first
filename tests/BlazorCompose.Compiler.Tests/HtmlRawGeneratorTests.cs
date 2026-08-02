namespace BlazorCompose.Compiler.Tests;

public sealed class HtmlRawGeneratorTests
{
    private const string RawLiteralSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            protected override View Body => Html.Main[Html.Raw("<em>hi</em>")];
        }
        """;

    private const string RawFieldSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            private string _html = "<em>hi</em>";
            protected override View Body => Html.Raw(_html);
        }
        """;

    private const string RawAsForEachContentSource = """
        using BlazorCompose;
        using System.Collections.Generic;

        public partial class C : ComposeComponentBase
        {
            private List<string> _xs = new();
            protected override View Body =>
                Html.ForEach(_xs, x => x, x => Html.Raw(x));
        }
        """;

    [Fact]
    public void Raw_WithLiteral_EmitsAddMarkupContent()
    {
        var result = CompilationTestHost.RunGenerator(RawLiteralSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // main(0) -> raw(1)
        Assert.Contains("__builder.OpenElement(0, \"main\")", generated);
        Assert.Contains("__builder.AddMarkupContent(1, \"<em>hi</em>\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Raw_WithFieldReference_EmitsAddMarkupContentOfField()
    {
        var result = CompilationTestHost.RunGenerator(RawFieldSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // Field references are emitted verbatim (no this-qualification), like other lowered expressions.
        Assert.Contains("__builder.AddMarkupContent(0, _html)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Raw_AsForEachContentRoot_ReportsBCF3003()
    {
        var result = CompilationTestHost.RunGenerator(RawAsForEachContentSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }
}

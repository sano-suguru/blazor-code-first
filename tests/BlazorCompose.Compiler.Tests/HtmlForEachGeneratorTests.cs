namespace BlazorCompose.Compiler.Tests;

public sealed class HtmlForEachGeneratorTests
{
    private const string ElementContentSource = """
        using BlazorCompose;
        using System.Collections.Generic;

        public partial class C : ComposeComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div(Html.ForEach(_items, x => x, x => Html.Span(x)));
        }
        """;

    private const string TextContentSource = """
        using BlazorCompose;
        using System.Collections.Generic;

        public partial class C : ComposeComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };
            protected override View Body =>
                Html.Div(Html.ForEach(_items, x => x, x => x));
        }
        """;

    [Fact]
    public void ForEach_WithElementContent_EmitsSetKeyOnElement()
    {
        var result = CompilationTestHost.RunGenerator(ElementContentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("foreach (var", generated);
        Assert.Contains("__builder.SetKey(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BC3003");
    }

    [Fact]
    public void ForEach_WithBareTextContent_ReportsBC3003()
    {
        var result = CompilationTestHost.RunGenerator(TextContentSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BC3003");
    }
}

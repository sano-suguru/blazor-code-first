namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ComponentAttributeShortcutGeneratorTests
{
    private const string TargetComponent =
        """
        public class Widget : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter(CaptureUnmatchedValues = true)]
            public System.Collections.Generic.IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
        }
        """;

    private const string TargetComponentWithTitleParameter =
        """
        public class TitledWidget : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter]
            public string? Title { get; set; }

            [Microsoft.AspNetCore.Components.Parameter(CaptureUnmatchedValues = true)]
            public System.Collections.Generic.IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
        }
        """;

    [Theory]
    [InlineData("Id", "id")]
    [InlineData("Type", "type")]
    [InlineData("Title", "title")]
    [InlineData("Role", "role")]
    [InlineData("Href", "href")]
    [InlineData("Src", "src")]
    [InlineData("Alt", "alt")]
    public void Shortcut_IsSugarForAttr(string shortcutName, string attributeName)
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body => Component<Widget>().{{shortcutName}}("value");
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains($"__builder.AddAttribute(1, \"{attributeName}\", \"value\");", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ShortcutNameCollidingWithDeclaredParameter_IsBCF3042()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponentWithTitleParameter}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body => Component<TitledWidget>().Title("hi");
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3042");
    }

    [Fact]
    public void ShortcutThenAttrSameName_IsBCF3010()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body =>
                    Component<Widget>().Id("a").Attr("id", "b");
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3010");
    }
}

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ComponentAttributeGeneratorTests
{
    private const string TargetComponent =
        """
        public class Widget : Microsoft.AspNetCore.Components.ComponentBase
        {
            [Microsoft.AspNetCore.Components.Parameter]
            public string? Title { get; set; }

            [Microsoft.AspNetCore.Components.Parameter(CaptureUnmatchedValues = true)]
            public System.Collections.Generic.IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
        }
        """;

    [Fact]
    public void Attr_EmitsAddAttribute_BeforeAddComponentParameter()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body =>
                    Component<Widget>().Attr("class", "primary").Param(c => c.Title, "hi");
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        var attrIndex = generated.IndexOf(
            "__builder.AddAttribute(1, \"class\", \"primary\");", StringComparison.Ordinal);
        var paramIndex = generated.IndexOf(
            "__builder.AddComponentParameter(2, \"Title\", ", StringComparison.Ordinal);
        Assert.True(attrIndex >= 0 && paramIndex >= 0 && attrIndex < paramIndex);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// The component surface's own bare-<c>.Attr</c> counterpart to
    /// <c>HtmlAttributeGeneratorTests.BareAttr_EmitsTheSameSourceAsAnExplicitTrue</c>: the one-argument
    /// arity here still needs <c>ValueParameter</c> routed through <c>SurfaceMethodKind.Attr</c>'s "name
    /// consumes argument 0" rule, or the value read for a bare <c>.Attr("disabled")</c> comes from the
    /// name argument's own slot instead of being synthesized as the omitted <see langword="true"/> (#487).
    /// </summary>
    [Fact]
    public void BareAttr_EmitsTheSameSourceAsAnExplicitTrue()
    {
        string Run(string body) => Assert.Single(CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body => {{body}};
            }
            """).GeneratedSources).SourceText.ToString();

        var bare = Run("""Component<Widget>().Attr("disabled")""");
        var explicitTrue = Run("""Component<Widget>().Attr("disabled", true)""");

        Assert.Equal(explicitTrue, bare);
    }

    [Fact]
    public void Class_IsSugarForAttrClass()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body => Component<Widget>().Class("primary");
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.AddAttribute(1, \"class\", \"primary\");", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void DuplicateAttrName_IsBCF3010()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body =>
                    Component<Widget>().Attr("class", "a").Attr("class", "b");
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3010");
    }

    [Fact]
    public void DuplicateAttrName_WithUnresolvedTypeArgument_ReportsBcf3010AndNotBcf3015()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body =>
                    Component<Widget>().Attr("class", "a").Attr("class", typeof(Unresolved).Name);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3010");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3015");
    }

    [Fact]
    public void ClassThenAttrClass_IsAlsoBCF3010_NoFolding()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body =>
                    Component<Widget>().Class("a").Attr("class", "b");
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3010");
    }

    [Fact]
    public void AttrNameCollidingWithDeclaredParameter_ExactCase_IsBCF3042()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body => Component<Widget>().Attr("Title", "hi");
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3042");
    }

    [Fact]
    public void AttrNameCollidingWithDeclaredParameter_DifferentCase_IsStillBCF3042()
    {
        // Measured: Blazor's own parameter binding matches names case-insensitively, so a
        // lowercase "title" would otherwise silently set [Parameter] Title at runtime, bypassing
        // .Param's type checking entirely. This is exactly the gap BCF3042 exists to close.
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body => Component<Widget>().Attr("title", "hi");
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3042");
    }

    [Fact]
    public void AttrNameCollidingWithDeclaredParameter_WithUnresolvedTypeArgument_ReportsBcf3042AndNotBcf3015()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body => Component<Widget>().Attr("Title", typeof(Unresolved).Name);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3042");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3015");
    }

    [Fact]
    public void AttrNameNotMatchingAnyParameter_CompilesClean()
    {
        var result = CompilationTestHost.RunGenerator(
            $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            {{TargetComponent}}

            public partial class TestComponent : BodyComponentBase
            {
                protected override View Body => Component<Widget>().Attr("data-x", "hi");
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("BCF", StringComparison.Ordinal));
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

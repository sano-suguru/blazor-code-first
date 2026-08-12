namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ForEachMethodGroupTests
{
    /// <summary>
    /// A <c>ForEach</c> whose content is the bare method group <c>Row</c>, declared with
    /// <c>$ATTRIBUTE$</c>.
    /// </summary>
    private const string MethodGroupHost = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body => Html.ForEach(_items, x => x, Row);

            $ATTRIBUTE$
            private static View Row(string item) => Html.Span[item];
        }
        """;

    [Fact]
    public void ForEachContent_WhenMethodGroupIsAViewPart_ExpandsStatically()
    {
        var result = CompilationTestHost.RunGenerator(
            MethodGroupHost.Replace("$ATTRIBUTE$", "[ViewPart]"));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3004");
        Assert.Contains("__builder.SetKey(", generated);
        Assert.Contains("OpenElement", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenMethodGroupBuildsFromTheSurfaceWithoutTheAttribute_ReportsBCF3030()
    {
        var result = CompilationTestHost.RunGenerator(
            MethodGroupHost.Replace("$ATTRIBUTE$", string.Empty));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3004");
    }

    [Fact]
    public void ForEachContent_WhenTheDelegateIsNotABareMethodGroup_ReportsBCF3004()
    {
        // A constructed delegate names no callee at the call site, so none of the three answers a bare
        // method group gets applies and the shape restriction stands.
        const string Source = """
            using BlazorCodeFirst;
            using System;
            using System.Collections.Generic;

            public partial class C : BodyComponentBase
            {
                private readonly List<string> _items = new() { "a", "b" };

                protected override View Body =>
                    Html.ForEach(_items, x => x, new Func<string, View>(Row));

                private static View Row(string item) => Html.Span[item];
            }
            """;

        var result = CompilationTestHost.RunGenerator(Source);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3004");
    }
}

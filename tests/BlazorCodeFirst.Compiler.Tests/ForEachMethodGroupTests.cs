namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ForEachMethodGroupTests
{
    private const string ViewPartSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body => Html.ForEach(_items, x => x, Row);

            [ViewPart]
            private static View Row(string item) => Html.Span[item];
        }
        """;

    private const string InertSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private readonly List<string> _items = new() { "a", "b" };

            protected override View Body => Html.ForEach(_items, x => x, Row);

            private static View Row(string item) => Html.Span[item];
        }
        """;

    private const string ConstructedDelegateSource = """
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

    [Fact]
    public void ForEachContent_WhenMethodGroupIsAViewPart_ExpandsStatically()
    {
        var result = CompilationTestHost.RunGenerator(ViewPartSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3004");
        Assert.Contains("__builder.SetKey(", generated);
        Assert.Contains("OpenElement", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenMethodGroupBuildsFromTheSurfaceWithoutTheAttribute_ReportsBCF3030()
    {
        var result = CompilationTestHost.RunGenerator(InertSource);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3004");
    }

    [Fact]
    public void ForEachContent_WhenTheDelegateIsNotABareMethodGroup_ReportsBCF3004()
    {
        // A constructed delegate is not a method group, so there is no callee to resolve at the call
        // site; the shape restriction stands.
        var result = CompilationTestHost.RunGenerator(ConstructedDelegateSource);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3004");
    }
}

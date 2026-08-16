using System.Globalization;

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

    [Fact]
    public void ForEachContent_WhenTheOpaqueMethodGroupIsAnInstanceMethod_CallsItOnTheComponentItself()
    {
        // The group was written with an implicit 'this' and the generated RenderView has the same one, so
        // the bare name is the spelling that works there. A containing-type qualification would name the
        // instance method as if it were static (#390).
        const string Source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using System.Collections.Generic;

            public partial class C : BodyComponentBase
            {
                private readonly List<string> _items = new() { "a" };

                protected override View Body => Html.ForEach(_items, null, Wrap);

                private View Wrap(string item)
                {
                    RenderFragment fragment = builder => builder.AddContent(0, item);
                    return fragment;
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(Source);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("global::C.Wrap(", generated);
        Assert.Contains("FragmentOf(Wrap(", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenTheOpaqueMethodGroupIsInaccessibleAtTheExpansionSite_ReportsBCF1002()
    {
        // The value path records what an expanded body has to be able to reach, and a method group names a
        // member on the same terms: the callee is inaccessible from the caller's type, so the expansion is
        // refused here rather than left to emit a CS0122 the author cannot trace back (#390).
        var result = CompilationTestHost.RunGenerator(
            ("Parts.cs", """
                using BlazorCodeFirst;
                using Microsoft.AspNetCore.Components;
                using System.Collections.Generic;

                public static class Parts
                {
                    public static readonly List<string> Items = new() { "a" };

                    [ViewPart]
                    public static View Rows() => Html.ForEach(Items, null, Wrap);

                    private static View Wrap(string item)
                    {
                        RenderFragment fragment = builder => builder.AddContent(0, item);
                        return fragment;
                    }
                }
                """),
            ("C.cs", """
                using BlazorCodeFirst;

                public partial class C : BodyComponentBase
                {
                    protected override View Body => Parts.Rows();
                }
                """));

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1002");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Wrap", message);
        Assert.Contains("not accessible", message);
        Assert.Empty(result.GeneratedSources);
    }
}

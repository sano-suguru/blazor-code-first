namespace BlazorCodeFirst.Compiler.Tests;

public sealed class HtmlFragmentGeneratorTests
{
    // Non-constant text, so the static fold does not absorb these frames: what this test checks is that a
    // fragment adds no wrapper frame and its children continue the enclosing sequence space.
    private const string FragmentInDivSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _a => "a";
            private string _b => "b";
            private string _c => "c";
            protected override View Body =>
                Html.Div[Html.Fragment(Html.Span[_a], Html.Span[_b]), Html.Span[_c]];
        }
        """;

    // Non-constant text: the point is that an empty fragment costs no sequence number, which needs the
    // following sibling's own frames to stay visible.
    private const string EmptyFragmentSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _after => "after";
            protected override View Body => Html.Div[Html.Fragment(), Html.Span[_after]];
        }
        """;

    private const string FragmentWithMixedAndRawSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body =>
                Html.Fragment("head", Html.Raw("<hr/>"), Html.Span["tail"]);
        }
        """;

    private const string FragmentAsForEachContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private List<string> _xs = new();
            protected override View Body =>
                Html.ForEach(_xs, x => x, x => Html.Fragment(Html.Span[x]));
        }
        """;

    private const string FragmentViaComposableAsForEachContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;

        public partial class C : BodyComponentBase
        {
            private List<string> _xs = new();
            [Composable] private static View Row(string x) => Html.Fragment(Html.Span[x]);
            protected override View Body =>
                Html.ForEach(_xs, x => x, x => Row(x));
        }
        """;

    private const string FragmentNonStaticChildSource = """
        using BlazorCodeFirst;
        using System;

        public partial class C : BodyComponentBase
        {
            private ReadOnlySpan<View> _kids => default;
            protected override View Body => Html.Fragment(_kids);
        }
        """;

    // Non-constant text: flattening is a sequence-space property — the inner fragment must not restart
    // numbering — so the individual frames have to stay observable.
    private const string FragmentInFragmentSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _a => "a";
            private string _b => "b";
            protected override View Body =>
                Html.Fragment(Html.Fragment(Html.Span[_a]), Html.Span[_b]);
        }
        """;

    // Non-constant text, for the reason EmptyFragmentSource gives.
    private const string EmptyFragmentInIfSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private bool _flag = true;
            private string _after => "after";
            protected override View Body =>
                Html.Div[Html.If(_flag, () => Html.Fragment()), Html.Span[_after]];
        }
        """;

    [Fact]
    public void Fragment_InDiv_EmitsChildrenWithoutWrapperElement()
    {
        var result = CompilationTestHost.RunGenerator(FragmentInDivSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> [fragment: span(1)->_a(2), span(3)->_b(4)] -> span(5)->_c(6). No extra element for the fragment.
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, _a)", generated);
        Assert.Contains("__builder.OpenElement(3, \"span\")", generated);
        Assert.Contains("__builder.AddContent(4, _b)", generated);
        Assert.Contains("__builder.OpenElement(5, \"span\")", generated);
        Assert.Contains("__builder.AddContent(6, _c)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void EmptyFragment_EmitsNothing_AndKeepsSiblingSequenceStable()
    {
        var result = CompilationTestHost.RunGenerator(EmptyFragmentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> empty fragment (width 0) -> span(1)->_after(2).
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, _after)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Fragment_WithMixedAndRawChildren_EmitsInPreorder()
    {
        var result = CompilationTestHost.RunGenerator(FragmentWithMixedAndRawSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // "head"(0), raw(1), the folded span(2). No wrapper. Preorder is the point, and the raw markup node
        // is what splits the two foldable runs: a lone text is one frame either way so it is not folded,
        // while the trailing span plus its text is two frames and folds into one.
        Assert.Contains("__builder.AddContent(0, \"head\")", generated);
        Assert.Contains("__builder.AddMarkupContent(1, \"<hr/>\")", generated);
        Assert.Contains("__builder.AddMarkupContent(2, \"<span>tail</span>\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Fragment_AsForEachContentRoot_ReportsBCF3003()
    {
        var result = CompilationTestHost.RunGenerator(FragmentAsForEachContentSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void Fragment_ViaComposableAsForEachContentRoot_ReportsBCF3003()
    {
        var result = CompilationTestHost.RunGenerator(FragmentViaComposableAsForEachContentSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }

    [Fact]
    public void Fragment_WithNonStaticChild_ReportsBCF1003()
    {
        // A non-analyzable child (a variable span, not an inline design-time syntax call) cannot be
        // sequenced, so the whole body fails to translate, proves fragment children must be
        // compile-time static.
        var result = CompilationTestHost.RunGenerator(FragmentNonStaticChildSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void FragmentNestedInFragment_FlattensWithNoWrapper()
    {
        var result = CompilationTestHost.RunGenerator(FragmentInFragmentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // inner fragment span(0)->_a(1), outer sibling span(2)->_b(3); neither fragment emits a wrapper.
        Assert.Contains("__builder.OpenElement(0, \"span\")", generated);
        Assert.Contains("__builder.AddContent(1, _a)", generated);
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, _b)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void EmptyFragmentInIfBranch_KeepsSiblingSequenceStable()
    {
        var result = CompilationTestHost.RunGenerator(EmptyFragmentInIfSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> OpenRegion(1){ empty fragment: nothing } -> span(2)->_after(3).
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenRegion(1)", generated);
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, _after)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

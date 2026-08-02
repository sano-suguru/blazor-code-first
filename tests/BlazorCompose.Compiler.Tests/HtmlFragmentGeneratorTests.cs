namespace BlazorCompose.Compiler.Tests;

public sealed class HtmlFragmentGeneratorTests
{
    private const string FragmentInDivSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            protected override View Body =>
                Html.Div[Html.Fragment(Html.Span["a"], Html.Span["b"]), Html.Span["c"]];
        }
        """;

    private const string EmptyFragmentSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            protected override View Body => Html.Div[Html.Fragment(), Html.Span["after"]];
        }
        """;

    private const string FragmentWithMixedAndRawSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            protected override View Body =>
                Html.Fragment("head", Html.Raw("<hr/>"), Html.Span["tail"]);
        }
        """;

    private const string FragmentAsForEachContentSource = """
        using BlazorCompose;
        using System.Collections.Generic;

        public partial class C : ComposeComponentBase
        {
            private List<string> _xs = new();
            protected override View Body =>
                Html.ForEach(_xs, x => x, x => Html.Fragment(Html.Span[x]));
        }
        """;

    private const string FragmentViaComposableAsForEachContentSource = """
        using BlazorCompose;
        using System.Collections.Generic;

        public partial class C : ComposeComponentBase
        {
            private List<string> _xs = new();
            [Composable] private static View Row(string x) => Html.Fragment(Html.Span[x]);
            protected override View Body =>
                Html.ForEach(_xs, x => x, x => Row(x));
        }
        """;

    private const string FragmentNonStaticChildSource = """
        using BlazorCompose;
        using System;

        public partial class C : ComposeComponentBase
        {
            private ReadOnlySpan<View> _kids => default;
            protected override View Body => Html.Fragment(_kids);
        }
        """;

    private const string FragmentInFragmentSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            protected override View Body =>
                Html.Fragment(Html.Fragment(Html.Span["a"]), Html.Span["b"]);
        }
        """;

    private const string EmptyFragmentInIfSource = """
        using BlazorCompose;

        public partial class C : ComposeComponentBase
        {
            private bool _flag = true;
            protected override View Body =>
                Html.Div[Html.If(_flag, () => Html.Fragment()), Html.Span["after"]];
        }
        """;

    [Fact]
    public void Fragment_InDiv_EmitsChildrenWithoutWrapperElement()
    {
        var result = CompilationTestHost.RunGenerator(FragmentInDivSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> [fragment: span(1)->"a"(2), span(3)->"b"(4)] -> span(5)->"c"(6). No extra element for the fragment.
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, \"a\")", generated);
        Assert.Contains("__builder.OpenElement(3, \"span\")", generated);
        Assert.Contains("__builder.AddContent(4, \"b\")", generated);
        Assert.Contains("__builder.OpenElement(5, \"span\")", generated);
        Assert.Contains("__builder.AddContent(6, \"c\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void EmptyFragment_EmitsNothing_AndKeepsSiblingSequenceStable()
    {
        var result = CompilationTestHost.RunGenerator(EmptyFragmentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> empty fragment (width 0) -> span(1)->"after"(2).
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, \"after\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Fragment_WithMixedAndRawChildren_EmitsInPreorder()
    {
        var result = CompilationTestHost.RunGenerator(FragmentWithMixedAndRawSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // "head"(0), raw(1), span(2)->"tail"(3). No wrapper.
        Assert.Contains("__builder.AddContent(0, \"head\")", generated);
        Assert.Contains("__builder.AddMarkupContent(1, \"<hr/>\")", generated);
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, \"tail\")", generated);
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
        // sequenced, so the whole body fails to translate — proves fragment children must be
        // compile-time static.
        var result = CompilationTestHost.RunGenerator(FragmentNonStaticChildSource);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void FragmentNestedInFragment_FlattensWithNoWrapper()
    {
        var result = CompilationTestHost.RunGenerator(FragmentInFragmentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // inner fragment span(0)->"a"(1), outer sibling span(2)->"b"(3); neither fragment emits a wrapper.
        Assert.Contains("__builder.OpenElement(0, \"span\")", generated);
        Assert.Contains("__builder.AddContent(1, \"a\")", generated);
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, \"b\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void EmptyFragmentInIfBranch_KeepsSiblingSequenceStable()
    {
        var result = CompilationTestHost.RunGenerator(EmptyFragmentInIfSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> OpenRegion(1){ empty fragment: nothing } -> span(2)->"after"(3).
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenRegion(1)", generated);
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, \"after\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

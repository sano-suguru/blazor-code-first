using System.Globalization;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The content-slot surface (#34, #176): a <c>[Composable]</c> part returning <c>SlotView</c> names
/// <c>Slot</c> where its caller's bracketed content belongs, and may take further slots as <c>View</c>
/// parameters.
/// </summary>
/// <remarks>
/// Every source here keeps its text non-constant. The static fold (#140) would otherwise collapse a
/// constant subtree into one <c>AddMarkupContent</c>, and what these tests are about is where the caller's
/// frames land in the callee's sequence space, which needs the individual frames to stay observable.
/// </remarks>
public sealed class ComposableContentGeneratorTests
{
    private const string CardSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private string _title => "Profile";
            private string _body => "text";

            [Composable] private static SlotView Card(string title) =>
                Div.Class("card")[H2[title], Slot];

            protected override View Body => Card(_title)[P[_body]];
        }
        """;

    private const string PanelSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private string _title => "Title";
            private string _body => "text";

            [Composable] private static SlotView Panel(View header) =>
                Div.Class("panel")[Div.Class("head")[header], Div.Class("body")[Slot]];

            protected override View Body => Panel(H2[_title])[P[_body]];
        }
        """;

    private const string MultipleChildrenSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private string _a => "a";
            private string _b => "b";

            [Composable] private static SlotView Card() => Div.Class("card")[Slot];

            protected override View Body => Card()[Span[_a], Span[_b]];
        }
        """;

    private const string ContentReferencedTwiceSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private string _a => "a";
            private string _b => "b";

            [Composable] private static SlotView Twice(View x) => Div[x, x, Slot];

            protected override View Body => Twice(Span[_a])[P[_b]];
        }
        """;

    private const string NestedCallInContentSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private string _a => "a";

            [Composable] private static SlotView Wrap() => Div[Slot];

            protected override View Body => Wrap()[Wrap()[Span[_a]]];
        }
        """;

    private const string RecursiveThroughOwnBodySource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private string _a => "a";

            [Composable] private static SlotView Loop() => Div[Loop()[Span["inner"]], Slot];

            protected override View Body => Loop()[Span[_a]];
        }
        """;

    private const string SlotInsideElementForEachContentSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private List<string> _xs = new();

            [Composable] private static SlotView Rows(List<string> xs) =>
                Ul[ForEach(xs, x => x, x => Li[Slot])];

            protected override View Body => Rows(_xs)[Span["cell"]];
        }
        """;

    private const string SlotAsForEachContentRootSource = """
        using BlazorCodeFirst;
        using System.Collections.Generic;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private List<string> _xs = new();

            [Composable] private static SlotView Rows(List<string> xs) =>
                Ul[ForEach(xs, x => x, x => Slot)];

            protected override View Body => Rows(_xs)[Li["cell"]];
        }
        """;

    [Fact]
    public void ContentTakingComposable_SplicesBracketContentAtTheSlot()
    {
        var result = CompilationTestHost.RunGenerator(CardSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> class(1) -> h2(2) -> title(3) -> [caller's content] p(4) -> _body(5).
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"card\")", generated);
        Assert.Contains("__builder.OpenElement(2, \"h2\")", generated);
        Assert.Contains("__builder.OpenElement(4, \"p\")", generated);
        Assert.Contains("__builder.AddContent(5, _body)", generated);

        // The value argument still becomes a typed local; only content skips that.
        Assert.Contains("__bcf_arg_0_0 = _title;", generated);
        Assert.Contains("__builder.AddContent(3, __bcf_arg_0_0)", generated);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContentTakingComposable_WithAnAdditionalViewParameter_FillsBothSlots()
    {
        var result = CompilationTestHost.RunGenerator(PanelSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) class(1) -> div(2) class(3) -> [header] h2(4) -> _title(5)
        //                 -> div(6) class(7) -> [bracket content] p(8) -> _body(9).
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"panel\")", generated);
        Assert.Contains("__builder.AddAttribute(3, \"class\", \"head\")", generated);
        Assert.Contains("__builder.OpenElement(4, \"h2\")", generated);
        Assert.Contains("__builder.AddContent(5, _title)", generated);
        Assert.Contains("__builder.AddAttribute(7, \"class\", \"body\")", generated);
        Assert.Contains("__builder.OpenElement(8, \"p\")", generated);
        Assert.Contains("__builder.AddContent(9, _body)", generated);

        // A View parameter is a content slot, so no local is declared for it: there is no value to bind.
        Assert.DoesNotContain("__bcf_arg_0_0", generated);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// Several children in the brackets are spliced as a group that opens no frame of its own, so they
    /// continue the callee's sequence space rather than restarting it.
    /// </summary>
    [Fact]
    public void ContentTakingComposable_WithSeveralChildren_SplicesThemInOrder()
    {
        var result = CompilationTestHost.RunGenerator(MultipleChildrenSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) class(1) -> span(2) -> _a(3) -> span(4) -> _b(5).
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, _a)", generated);
        Assert.Contains("__builder.OpenElement(4, \"span\")", generated);
        Assert.Contains("__builder.AddContent(5, _b)", generated);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A <c>View</c> parameter is an ordinary parameter, so a body may name it more than once. Nothing is
    /// captured or shared: each reference expands the caller's subtree again, which is the same property a
    /// Blazor <c>RenderFragment</c> invoked twice has, and each expansion consumes its own preorder ordinals
    /// so the generated names cannot collide.
    /// </summary>
    [Fact]
    public void ViewParameter_ReferencedTwice_EmitsTheContentTwice()
    {
        var result = CompilationTestHost.RunGenerator(ContentReferencedTwiceSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> span(1) -> _a(2) -> span(3) -> _a(4) -> p(5) -> _b(6).
        Assert.Contains("__builder.OpenElement(1, \"span\")", generated);
        Assert.Contains("__builder.AddContent(2, _a)", generated);
        Assert.Contains("__builder.OpenElement(3, \"span\")", generated);
        Assert.Contains("__builder.AddContent(4, _a)", generated);
        Assert.Contains("__builder.OpenElement(5, \"p\")", generated);
        Assert.Contains("__builder.AddContent(6, _b)", generated);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// The case that decides where a content argument's cycle stack comes from. <c>Wrap()[Wrap()[…]]</c> is
    /// not recursion: the inner call is an expression written at the outer call site, so expanding it under
    /// the callee's own stack would report a cycle that does not exist.
    /// </summary>
    [Fact]
    public void SameComposableNestedInsideItsOwnContent_IsNotACycle()
    {
        var result = CompilationTestHost.RunGenerator(NestedCallInContentSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        // div(0) -> div(1) -> span(2) -> _a(3).
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(1, \"div\")", generated);
        Assert.Contains("__builder.OpenElement(2, \"span\")", generated);
        Assert.Contains("__builder.AddContent(3, _a)", generated);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// The other half of the test above: a part that calls itself from its own <em>body</em> still is a
    /// cycle, so relaxing the stack for content arguments did not disable the check.
    /// </summary>
    [Fact]
    public void ComposableCallingItselfFromItsOwnBody_ReportsBCF1002()
    {
        var result = CompilationTestHost.RunGenerator(RecursiveThroughOwnBodySource);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains("cycle", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A slot nested inside an element is unaffected by the keyability rule: the element is the content root
    /// and carries the key.
    /// </summary>
    [Fact]
    public void SlotNestedInsideForEachContent_IsKeyable()
    {
        var result = CompilationTestHost.RunGenerator(SlotInsideElementForEachContentSource);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3003");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A slot that <em>is</em> a ForEach content root gets the existing BCF3003, because the caller decides
    /// what arrives there and <c>KeyabilityResolver</c> is reachability-independent by design: the same
    /// definition may be called with a keyable element from one site and a bare <c>If</c> from another, so
    /// the declaration cannot promise a keyable frame.
    /// </summary>
    [Fact]
    public void SlotAsForEachContentRoot_ReportsBCF3003()
    {
        var result = CompilationTestHost.RunGenerator(SlotAsForEachContentRootSource);

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3003");
    }
}

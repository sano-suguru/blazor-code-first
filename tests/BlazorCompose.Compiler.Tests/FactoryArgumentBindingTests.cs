namespace BlazorCompose.Compiler.Tests;

// #36: the analyzer read factory arguments by syntactic position, so named arguments written out of
// declaration order bound to the wrong parameter. Two forms compiled AND generated, producing working
// but wrong code — those are the cases these tests exist for. The assertion is source equality against
// the positional spelling, because a diagnostic-based assertion cannot see a silent swap.
public sealed class FactoryArgumentBindingTests
{
    private static string GenerateBody(string bodyExpression)
    {
        var source = $$"""
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Counter : ComposeComponentBase
            {
                private bool _on;

                protected override View Body => {{bodyExpression}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        CompilationTestHost.AssertOutputCompiles(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    [Fact]
    public void If_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        // Before the fix this silently inverted the branches: Arguments[1] (otherwise) was read as
        // then, and Arguments[2] (then) as otherwise. Both are valid Func<View>, so nothing complained.
        var named = GenerateBody("""If(_on, otherwise: () => Span("No"), then: () => Span("Yes"))""");
        var positional = GenerateBody("""If(_on, () => Span("Yes"), () => Span("No"))""");

        Assert.Equal(positional, named);
    }

    [Fact]
    public void Attr_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        // Before the fix TryGetConstantName(args[0]) succeeded on the VALUE, so the attribute name and
        // value were swapped in the emitted AddAttribute call.
        var named = GenerateBody("""Div().Attr(value: "1", name: "data-x")""");
        var positional = GenerateBody("""Div().Attr("data-x", "1")""");

        Assert.Equal(positional, named);
    }

    [Fact]
    public void On_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        var named = GenerateBody("""Div().On(handler: () => { }, eventName: "onclick")""");
        var positional = GenerateBody("""Div().On("onclick", () => { })""");

        Assert.Equal(positional, named);
    }

    [Fact]
    public void If_OmittedOptionalOtherwise_StillGenerates()
    {
        // Regression guard: the presence of `otherwise` was decided by Arguments.Count >= 3 and is now
        // decided by whether an argument bound to that parameter, so the omitted case must still work.
        var generated = GenerateBody("""If(_on, () => Span("Yes"))""");

        Assert.Contains("protected override void RenderView(", generated);
    }

    [Fact]
    public void If_ExplicitNullOtherwise_StillGenerates()
    {
        var generated = GenerateBody("""If(_on, () => Span("Yes"), null)""");

        Assert.Contains("protected override void RenderView(", generated);
    }

    [Fact]
    public void Decoration_StaticCallForm_RemainsUnsupported()
    {
        // Deliberately unchanged (spec non-goal): with arguments bound by parameter the receiver is
        // available as argument 0, so supporting Decorations.Attr(view, ...) would be new capability
        // rather than a bug fix. Pin the current behaviour so it is not enabled by accident.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Counter : ComposeComponentBase
            {
                protected override View Body => Decorations.Attr(Div(), "data-x", "1");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "BC1003");
    }

    [Fact]
    public void ArgumentBinding_OutOfPositionNamedArgumentFollowedByPositional_IsRejectedByTheLanguage()
    {
        // FactoryArguments relies on Roslyn's binding rather than reimplementing the argument-position
        // rule. The language guarantee that makes any position-based reading equivalent for legal code
        // is CS8323; pin it, because the whole design leans on the compiler rejecting this form.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Counter : ComposeComponentBase
            {
                private bool _on;

                protected override View Body => If(then: () => Span("Yes"), _on);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.OutputCompilation.GetDiagnostics(), d => d.Id == "CS8323");
    }

    [Fact]
    public void ForEach_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        // The form issue #36 cites. Unlike If and Attr this one does not silently miscompile — the
        // swapped key/content lands on BC3003 or BC1003 — but it must still bind correctly.
        const string named = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            using System.Collections.Generic;

            public partial class Counter : ComposeComponentBase
            {
                private List<string> _items = new();

                protected override View Body =>
                    Div(ForEach(_items, content: i => Li(i), key: i => i));
            }
            """;

        const string positional = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            using System.Collections.Generic;

            public partial class Counter : ComposeComponentBase
            {
                private List<string> _items = new();

                protected override View Body =>
                    Div(ForEach(_items, i => i, i => Li(i)));
            }
            """;

        var namedSource = Assert.Single(CompilationTestHost.RunGenerator(named).GeneratedSources)
            .SourceText.ToString();
        var positionalSource = Assert.Single(CompilationTestHost.RunGenerator(positional).GeneratedSources)
            .SourceText.ToString();

        Assert.Equal(positionalSource, namedSource);
    }

    [Fact]
    public void Element_TagNamed_GeneratesSameSourceAsPositional()
    {
        // `tag` can only be named in its natural first position: children bind to the expanded
        // `params` parameter, and C# does not allow naming an expanded params argument. So there is
        // no legal spelling that names `tag` after the children — this covers naming the first
        // argument, not reordering past children.
        var named = GenerateBody("""Element(tag: "section", "a", "b")""");
        var positional = GenerateBody("""Element("section", "a", "b")""");

        Assert.Equal(positional, named);
    }

    [Fact]
    public void Param_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        const string named = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;

            public sealed class Widget : ComponentBase
            {
                [Parameter] public string Label { get; set; } = "";
            }

            public partial class Counter : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Widget>().Param(value: "x", selector: c => c.Label);
            }
            """;

        const string positional = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;

            public sealed class Widget : ComponentBase
            {
                [Parameter] public string Label { get; set; } = "";
            }

            public partial class Counter : ComposeComponentBase
            {
                protected override View Body =>
                    Component<Widget>().Param(c => c.Label, "x");
            }
            """;

        var namedSource = Assert.Single(CompilationTestHost.RunGenerator(named).GeneratedSources)
            .SourceText.ToString();
        var positionalSource = Assert.Single(CompilationTestHost.RunGenerator(positional).GeneratedSources)
            .SourceText.ToString();

        Assert.Equal(positionalSource, namedSource);
    }

    [Fact]
    public void Element_ExplicitChildrenCollection_RemainsUnsupported()
    {
        // `Div(children: arr)` is legal C# (View[] converts to ReadOnlySpan<View>) but the argument is
        // one whole collection, not a child list. It must land on BC1003 rather than be mis-split.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Counter : ComposeComponentBase
            {
                private static View[] Kids() => new View[] { Span("a") };

                protected override View Body => Div(children: Kids());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "BC1003");
    }
}

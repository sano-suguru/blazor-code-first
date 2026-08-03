using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Compiler.Tests;

// #36: the analyzer read factory arguments by syntactic position, so named arguments written out of
// declaration order bound to the wrong parameter. Two forms compiled AND generated, producing working
// but wrong code, those are the cases these tests exist for. The assertion is source equality against
// the positional spelling, because a diagnostic-based assertion cannot see a silent swap.
public sealed class FactoryArgumentBindingTests
{
    private static string GenerateBody(string bodyExpression)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
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
        var named = GenerateBody("""If(_on, otherwise: () => Span["No"], then: () => Span["Yes"])""");
        var positional = GenerateBody("""If(_on, () => Span["Yes"], () => Span["No"])""");

        Assert.Equal(positional, named);
    }

    [Fact]
    public void Attr_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        // Before the fix TryGetConstantName(args[0]) succeeded on the VALUE, so the attribute name and
        // value were swapped in the emitted AddAttribute call.
        var named = GenerateBody("""Div.Attr(value: "1", name: "data-x")""");
        var positional = GenerateBody("""Div.Attr("data-x", "1")""");

        Assert.Equal(positional, named);
    }

    [Fact]
    public void On_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        var named = GenerateBody("""Div.On(handler: () => { }, eventName: "onclick")""");
        var positional = GenerateBody("""Div.On("onclick", () => { })""");

        Assert.Equal(positional, named);
    }

    [Fact]
    public void If_OmittedOptionalOtherwise_StillGenerates()
    {
        // Regression guard: the presence of `otherwise` was decided by Arguments.Count >= 3 and is now
        // decided by whether an argument bound to that parameter, so the omitted case must still work.
        var generated = GenerateBody("""If(_on, () => Span["Yes"])""");

        Assert.Contains("protected override void RenderView(", generated);
    }

    [Fact]
    public void If_ExplicitNullOtherwise_StillGenerates()
    {
        var generated = GenerateBody("""If(_on, () => Span["Yes"], null)""");

        Assert.Contains("protected override void RenderView(", generated);
    }

    [Fact]
    public void Decoration_StaticCallForm_RemainsUnsupported()
    {
        // Deliberately unchanged (spec non-goal): with arguments bound by parameter the receiver is
        // available as argument 0, so supporting Decorations.Attr(view, ...) would be new capability
        // rather than a bug fix. Pin the current behaviour so it is not enabled by accident.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Decorations.Attr(Div, "data-x", "1");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void ArgumentBinding_OutOfPositionNamedArgumentFollowedByPositional_IsRejectedByTheLanguage()
    {
        // FactoryArguments relies on Roslyn's binding rather than reimplementing the argument-position
        // rule. The language guarantee that makes any position-based reading equivalent for legal code
        // is CS8323; pin it, because the whole design leans on the compiler rejecting this form.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private bool _on;

                protected override View Body => If(then: () => Span["Yes"], _on);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.OutputCompilation.GetDiagnostics(), d => d.Id == "CS8323");
    }

    [Fact]
    public void ForEach_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        // The form issue #36 cites. Unlike If and Attr this one does not silently miscompile, the
        // swapped key/content lands on BCF3003 or BCF1003, but it must still bind correctly.
        const string named = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using System.Collections.Generic;

            public partial class Counter : BodyComponentBase
            {
                private List<string> _items = new();

                protected override View Body =>
                    Div[ForEach(_items, content: i => Li[i], key: i => i)];
            }
            """;

        const string positional = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            using System.Collections.Generic;

            public partial class Counter : BodyComponentBase
            {
                private List<string> _items = new();

                protected override View Body =>
                    Div[ForEach(_items, i => i, i => Li[i])];
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
        // `Element` now takes only `tag`; children bind separately through the indexer. `tag` has no
        // other parameter to be reordered against, so this just pins that naming it changes nothing.
        var named = GenerateBody("""Element(tag: "section")["a", "b"]""");
        var positional = GenerateBody("""Element("section")["a", "b"]""");

        Assert.Equal(positional, named);
    }

    [Fact]
    public void Param_NamedArgumentsOutOfOrder_GeneratesSameSourceAsPositional()
    {
        const string named = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public sealed class Widget : ComponentBase
            {
                [Parameter] public string Label { get; set; } = "";
            }

            public partial class Counter : BodyComponentBase
            {
                protected override View Body =>
                    Component<Widget>().Param(value: "x", selector: c => c.Label);
            }
            """;

        const string positional = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public sealed class Widget : ComponentBase
            {
                [Parameter] public string Label { get; set; } = "";
            }

            public partial class Counter : BodyComponentBase
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
        // `Div[children: arr]` is legal C# (View[] converts to ReadOnlySpan<View>) but the argument is
        // one whole collection, not a child list. It must land on BCF1003 rather than be mis-split.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private static View[] Kids() => new View[] { Span["a"] };

                protected override View Body => Div[children: Kids()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void Fragment_ExplicitChildrenCollection_RemainsUnsupported()
    {
        // Same guard as Element_ExplicitChildrenCollection_RemainsUnsupported, pinned on Fragment's own
        // HasUnanalyzableParamsArgument check so that copy keeps its only test once the pair is duplicated.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private static View[] Kids() => new View[] { Span["a"] };

                protected override View Body => Fragment(children: Kids());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.GeneratedSources);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void Div_NullForgivingChild_PreservesTheSuppressionInGeneratedSource()
    {
        // Mirrors the decoration-side fixture (NullClassComponent's `Class(NullClass!)`): a bare
        // null-forgiving child with nothing else to convert has its `!` elided from the operation
        // tree by Roslyn, so a naive read of the params element's Syntax silently drops the
        // suppression the author wrote. Assert it survives into the generated child expression.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private static string? NullText => null;

                protected override View Body => Div[NullText!];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        CompilationTestHost.AssertOutputCompiles(result);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("NullText!", generated);
    }

    [Fact]
    public void Element_CollectionExpressionLiteralChildren_GeneratesSameSourceAsExpanded()
    {
        // #75: a collection expression in the non-expanded position binds the literal to the params
        // indexer as one whole collection rather than two children. Under the params-collections rules
        // it is the same call as the expanded spelling and passes the same span, so it must emit the
        // same code.
        var literal = GenerateBody("""Div[[Span["a"], Span["b"]]]""");
        var expanded = GenerateBody("""Div[Span["a"], Span["b"]]""");

        Assert.Equal(expanded, literal);

        // Equality alone would also hold for two childless divs, so pin that the children are really
        // there: two spans, each with its own text at its own sequence number.
        Assert.Equal(2, Regex.Count(literal, """OpenElement\(\d+, "span"\)"""));
        Assert.Contains("""AddContent(2, "a")""", literal);
        Assert.Contains("""AddContent(4, "b")""", literal);
    }

    [Fact]
    public void Element_NamedCollectionExpressionLiteralChildren_GeneratesSameSourceAsPositional()
    {
        // The named spelling is this file's own subject (#36), and #75 makes a new combination legal:
        // a literal passed by parameter name. It binds through the same params slot, so it must mean the
        // same thing, pinned here so a future change that keys the literal on argument position rather
        // than on the bound parameter cannot pass silently.
        var named = GenerateBody("""Div[children: ["a", "b"]]""");
        var positional = GenerateBody("""Div["a", "b"]""");

        Assert.Equal(positional, named);
        Assert.Contains("""AddContent(1, "a")""", named);
        Assert.Contains("""AddContent(2, "b")""", named);
    }

    [Fact]
    public void Element_EmptyCollectionExpressionLiteral_GeneratesAChildlessElement()
    {
        // `Div[[]]` passes an empty span, which is the same call as writing no children at all.
        var literal = GenerateBody("""Div[[]]""");
        var childless = GenerateBody("""Div""");

        Assert.Equal(childless, literal);
    }

    [Fact]
    public void Element_NullForgivingChildInALiteral_PreservesTheSuppressionInGeneratedSource()
    {
        // The nested counterpart of Div_NullForgivingChild_PreservesTheSuppressionInGeneratedSource:
        // Roslyn elides a bare null-forgiving suppression from the operation tree, so the element's
        // Syntax points at the inner operand. Recovery has to climb to the written expression, and in
        // a literal that container is an ExpressionElementSyntax, not an ArgumentSyntax.
        var literal = GenerateNullForgivingBody("""Div[[NullText!, "b"]]""");
        var expanded = GenerateNullForgivingBody("""Div[NullText!, "b"]""");

        Assert.Contains("NullText!", literal);

        // Two elements, so a recovery rule that resolved every element to the whole outer argument would
        // emit the literal twice instead of one child each, which equality against the expanded
        // spelling catches, and the per-child assertions then name.
        Assert.Equal(expanded, literal);
        Assert.Contains("""AddContent(1, global::Counter.NullText!)""", literal);
        Assert.Contains("""AddContent(2, "b")""", literal);
    }

    /// <summary>
    /// As <see cref="GenerateBody"/>, for the bodies that need a nullable member to suppress. Kept
    /// separate rather than folded into that host: adding a member there would move every existing
    /// baseline in this file.
    /// </summary>
    private static string GenerateNullForgivingBody(string bodyExpression)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private static string? NullText => null;

                protected override View Body => {{bodyExpression}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        CompilationTestHost.AssertOutputCompiles(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }
}

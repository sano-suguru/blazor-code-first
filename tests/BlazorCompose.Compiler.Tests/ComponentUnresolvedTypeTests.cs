using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class ComponentUnresolvedTypeTests
{
    private static int CountBC3012(GeneratorRunResult result) =>
        result.Diagnostics.Count(static d => d.Id == "BC3012");

    [Fact]
    public void Component_WithParam_ReportsBC3012AndNotBC1003()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Probe>().Param(p => p.Label, "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC1003");
        Assert.DoesNotContain(result.GeneratedSources, static s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void Component_WithParamThenDecoration_ReportsBC3012()
    {
        // The outer .Class call makes GetSymbolInfo on both outer invocations return null, so the
        // analyzer exits before its Component branch. Only a sweep on the failure path sees this.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Probe>().Param(p => p.Label, "x").Class("c");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
    }

    [Fact]
    public void Component_InsideIfLambda_ReportsBC3012()
    {
        // If's lambda argument degrades the outer GetSymbolInfo to null (OverloadResolutionFailure),
        // so the analyzer never recurses into the lambda body.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => If(true, () => Component<Probe>());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
    }

    [Fact]
    public void Component_InsideForEachLambda_ReportsBC3012()
    {
        const string source = """
            using System.Collections.Generic;
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                private readonly List<string> _items = [];
                protected override View Body =>
                    ForEach(_items, key: i => i, content: i => Component<Probe>().Param(p => p.Label, i));
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
    }

    [Fact]
    public void Component_WithNestedUnresolvedTypeArgument_ReportsBC3012()
    {
        const string source = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;
            namespace T;
            public class Wrapper<TRow> : ComponentBase { }
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Wrapper<Missing>>().Param(w => w.Label, "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
    }

    [Fact]
    public void Component_QualifiedHtmlSpelling_ReportsBC3012()
    {
        const string source = """
            using BlazorCompose;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Html.Component<Probe>().Param(p => p.Label, "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
    }

    [Fact]
    public void Component_InsideComposableBody_ReportsBC3012NotGenericBC1002()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public static class Frags
            {
                [Composable]
                public static View Card() => Div(Component<Probe>().Param(p => p.Label, "x"));
            }
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Frags.Card();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC1002");
    }
}

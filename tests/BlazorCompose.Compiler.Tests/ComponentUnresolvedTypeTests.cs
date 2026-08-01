using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

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
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
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
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
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
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
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
                public static View Card() => Div[Component<Probe>().Param(p => p.Label, "x")];
            }
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Frags.Card();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC1002");
    }

    [Fact]
    public void Component_WithoutParam_ReportsBC3012AndNoSource()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Probe>();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
        Assert.DoesNotContain(result.GeneratedSources, static s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void Component_TwiceInOneBody_ReportsBC3012PerInvocation()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Div[Component<Probe>(), Component<Probe>()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(2, CountBC3012(result));
    }

    [Fact]
    public void Component_TwoDistinctUnresolvedTypes_ReportsBC3012ForEach()
    {
        // Guards against a future dedup-by-type-name regression that would pass the same-type test
        // above. Measured behavior: two diagnostics at distinct locations.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Div[Component<Alpha>(), Component<Beta>()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(2, CountBC3012(result));
    }

    [Fact]
    public void Component_WithUnresolvedContainingType_ReportsBC3012()
    {
        // The type argument itself is a resolved TypeKind.Class with an EMPTY TypeArguments list; the
        // unresolved type is only reachable through its ContainingType. This shape needs the analyzer's
        // bail even with a .Param, because `Inner.Label` is a real settable [Parameter] — the selector
        // binds, translation would otherwise succeed, and the generator would emit
        // OpenComponent<global::T.Outer<Missing>.Inner>, failing with CS0246 in generated code.
        const string source = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;
            namespace T;
            public class Outer<TX>
            {
                public class Inner : ComponentBase
                {
                    [Parameter] public string Label { get; set; } = "";
                }
            }
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Outer<Missing>.Inner>().Param(i => i.Label, "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
    }

    [Fact]
    public void Component_WithExplicitParamTypeArgument_ReportsBC3012AndNotBC3005()
    {
        // An explicit TValue makes the outer .Param call resolve, so it reaches the Param branch and
        // would otherwise draw a spurious BC3005 about a selector that cannot bind to an unresolved type.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Probe>().Param<string>(p => p.Label, "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3005");
    }

    [Fact]
    public void Component_WithResolvedType_ReportsNoBC3012()
    {
        const string source = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;
            namespace T;
            public class Real : ComponentBase
            {
                [Parameter] public string Label { get; set; } = "";
            }
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Real>().Param(r => r.Label, "x");
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Empty(result.Diagnostics);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Component_WithTypeParameter_ReportsNoBC3012()
    {
        // A generic component embedding its own type parameter is supported and must not be reported:
        // a type parameter is not an unresolved type.
        const string source = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host<TChild> : ComposeComponentBase where TChild : IComponent
            {
                protected override View Body => Component<TChild>();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3012");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("OpenComponent<TChild>", generated);
    }

    [Fact]
    public void Component_FromMetadataReference_ReportsNoBC3012()
    {
        // The regression guard for "the constraint is same-compilation only": a component that exists
        // only in a referenced assembly — the position a .razor component in a referenced project or
        // NuGet package occupies — resolves normally and is emitted fully qualified.
        const string library = """
            using Microsoft.AspNetCore.Components;
            namespace Lib;
            public class LibProbe : ComponentBase
            {
                [Parameter] public string Label { get; set; } = "";
            }
            """;

        var reference = CompilationTestHost.CompileToMetadataReference(library, "LibAssembly");

        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Lib.LibProbe>().Param(p => p.Label, "x");
            }
            """;

        var compilation = CompilationTestHost.CreateCompilation([("Host.cs", host)], reference);
        var result = CompilationTestHost.RunGenerator(compilation);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3012");
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("OpenComponent<global::Lib.LibProbe>", generated);
    }

    [Fact]
    public void Component_ThroughUsingAlias_ReportsWrittenName()
    {
        // Through an alias, ToDisplayString recovers the alias TARGET, so the message argument must come
        // from the written syntax: 'Aliased' is what the author wrote and must fix.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            using Aliased = T.Missing;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Aliased>();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3012");
        Assert.Contains("'Aliased'", diagnostic.GetMessage());
    }

    [Fact]
    public void BC3012_Message_NamesTheNestedTypeArgumentAsWritten()
    {
        const string source = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;
            namespace T;
            public class Wrapper<TRow> : ComponentBase { }
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Wrapper<Missing>>();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3012");
        Assert.Contains("'Wrapper<Missing>'", diagnostic.GetMessage());
        Assert.Contains(".razor", diagnostic.GetMessage());
    }

    [Fact]
    public void Component_InsideLayoutChrome_ReportsBC3012()
    {
        // The design-time expression name is resolved from the base symbol, so a layout's Chrome takes
        // the same path as a component's Body. Pinned because the spec lists Chrome among the shapes
        // whose previously-green build this diagnostic deliberately breaks.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Shell : ComposeLayoutBase
            {
                protected override View Chrome => Div[Component<Probe>()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void BC3012_Location_CoversTheTypeArgumentSyntax()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Probe>();
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BC3012");

        // DiagnosticInfo.ToDiagnostic rebuilds an ExternalFile-kind Location whose SourceTree is null,
        // so the span has to be sliced out of a reconstructed SourceText rather than read from the tree.
        var text = SourceText.From(source);
        Assert.Equal("Probe", text.ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void ComponentWithChildren_UnresolvedType_ReportsBC3012AndNotBC1003()
    {
        // Probe is never declared: the same shape the parameterless overload already covers, but the
        // scanner compares against a single symbol, so the params form would fall through to BC1003.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Probe>()[Div["x"]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }

    [Fact]
    public void ComponentWithChildren_UnresolvedTypeInsideIf_ReportsBC3012Once()
    {
        // A lambda argument degrades GetSymbolInfo to a non-method symbol, so only the failure-path
        // sweep sees this. Exactly one report, not one per enclosing invocation.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => If(true, () => Component<Probe>()[Div["x"]]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Equal(1, CountBC3012(result));
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BC3015");
    }
}

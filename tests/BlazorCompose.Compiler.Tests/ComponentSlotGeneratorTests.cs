using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class ComponentSlotGeneratorTests
{
    private const string CardSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Card : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter] public RenderFragment? Footer { get; set; }
        }
        """;

    [Fact]
    public void ComponentWithChildren_CompilesWithoutCSharpErrors()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Card>(Div("x")).Param(c => c.Title, "t");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));

        // The new overloads must exist on the runtime surface: any CS error here means the API is missing.
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
            d => d.Id.StartsWith("CS", System.StringComparison.Ordinal));
    }

    [Fact]
    public void FragmentParamOverload_CompilesWithoutCSharpErrors()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Card>().Param(c => c.Footer, Div("f"));
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));

        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
            d => d.Id.StartsWith("CS", System.StringComparison.Ordinal));
    }

    private static string GeneratedHost(GeneratorRunResult result) =>
        result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();

    [Fact]
    public void ComponentWithChildren_BindsChildContentSlot()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Card>(Div("x")).Param(c => c.Title, "t");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("__builder.OpenComponent<global::T.Card>(0);", code);
        Assert.Contains("__builder.AddComponentParameter(1, \"Title\", \"t\");", code);
        Assert.Contains(
            "__builder.AddComponentParameter(2, \"ChildContent\", "
                + "(global::Microsoft.AspNetCore.Components.RenderFragment)((__builder) =>",
            code);
        Assert.Contains("__builder.OpenElement(3, \"div\");", code);
        Assert.Contains("__builder.AddContent(4, \"x\");", code);
    }

    [Fact]
    public void ComponentWithMultipleChildren_PutsThemAllInOneSlot()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Card>(Div("a"), "text");
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        // Both children share the single ChildContent fragment; only one AddComponentParameter for it.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(code, @"""ChildContent"""));
        Assert.Contains("__builder.OpenElement(2, \"div\");", code);
        Assert.Contains("__builder.AddContent(4, \"text\");", code);
    }

    [Fact]
    public void FragmentParam_BindsNamedSlot()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Card>().Param(c => c.Footer, Div("f"));
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(
            "__builder.AddComponentParameter(1, \"Footer\", "
                + "(global::Microsoft.AspNetCore.Components.RenderFragment)((__builder) =>",
            code);
        Assert.Contains("__builder.OpenElement(2, \"div\");", code);
    }

    [Fact]
    public void FragmentParam_ChildContentByName_IsAlsoAccepted()
    {
        // Razor permits the equivalent attribute form, so this spelling is legal, just verbose.
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Card>().Param(c => c.ChildContent, Div("x"));
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("\"ChildContent\"", GeneratedHost(result));
    }

    [Fact]
    public void ComponentWithChildren_NestedSlots_ContinueTheFlatSequence()
    {
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => Component<Card>(Component<Card>(Div("deep")));
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // outer OpenComponent=0, outer slot=1, inner OpenComponent=2, inner slot=3, div=4, text=5
        Assert.Contains("__builder.OpenComponent<global::T.Card>(0);", code);
        Assert.Contains("__builder.OpenComponent<global::T.Card>(2);", code);
        Assert.Contains("__builder.OpenElement(4, \"div\");", code);
        Assert.Contains("__builder.AddContent(5, \"deep\");", code);
    }

    [Fact]
    public void ComponentWithChildren_RealRenderFragmentValue_StillUsesTheScalarChannel()
    {
        // A genuine RenderFragment binds through the generic Param and is emitted verbatim, not wrapped
        // in a lambda.
        const string host = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                [Parameter] public RenderFragment? Incoming { get; set; }
                protected override View Body => Component<Card>().Param(c => c.ChildContent, Incoming);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("__builder.AddComponentParameter(1, \"ChildContent\", Incoming);", code);
        Assert.DoesNotContain("RenderFragment)((__builder)", code);
    }

    [Fact]
    public void ComponentWithChildren_ExplicitCollectionArgument_IsRejected()
    {
        // One whole collection passed to the params parameter is not a child list; mirrors Div(children: arr).
        const string host = """
            using BlazorCompose;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                private static readonly View[] _kids = [];
                protected override View Body => Component<Card>(children: _kids);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BC1003");
    }
}

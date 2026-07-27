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
            d => d.Id.StartsWith("CS", System.StringComparison.Ordinal) && d.Id != "CS0534");
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
            d => d.Id.StartsWith("CS", System.StringComparison.Ordinal) && d.Id != "CS0534");
    }
}

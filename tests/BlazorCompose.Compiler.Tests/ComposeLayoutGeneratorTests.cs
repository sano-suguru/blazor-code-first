using System.Threading.Tasks;
using BlazorCompose.Compiler.Diagnostics;

namespace BlazorCompose.Compiler.Tests;

public sealed class ComposeLayoutGeneratorTests
{
    [Fact]
    public void Generator_ComposeLayout_EmitsRenderViewFromChrome()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Shell : ComposeLayoutBase
            {
                protected override View Chrome => Main(Body);
            }
            """;

        var generated = Assert.Single(CompilationTestHost.RunGenerator(source).GeneratedSources).SourceText.ToString();

        Assert.Contains("protected override void RenderView(", generated);
        Assert.Contains("__builder.OpenElement(0, \"main\");", generated);
        Assert.Contains("__builder.AddContent(1, Body);", generated);
    }

    [Fact]
    public async Task Generator_NonPartialComposeLayout_ReportsBC1001()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public class Shell : ComposeLayoutBase
            {
                protected override View Chrome => Main(Body);
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(source);

        Assert.Contains(diagnostics, d => d.Id == "BC1001");
    }

    [Fact]
    public async Task Generator_MutationInsideChrome_ReportsBC3001()
    {
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Shell : ComposeLayoutBase
            {
                private int _n;
                protected override View Chrome => Main(Span($"{_n++}"));
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.Contains(diagnostics, d => d.Id == "BC3001");
    }

    [Fact]
    public void Generator_PropertyNamedBodyOnALayout_IsNotTreatedAsTheExpression()
    {
        // Regression guard for the semantic lookup: a layout's Body is Blazor's RenderFragment
        // parameter, not the design-time expression. Only Chrome is.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Shell : ComposeLayoutBase
            {
                protected override View Chrome => Main(Body);
            }
            """;

        var generated = Assert.Single(CompilationTestHost.RunGenerator(source).GeneratedSources).SourceText.ToString();

        // Exactly one RenderView is emitted, driven by Chrome.
        Assert.Contains("__builder.AddContent(1, Body);", generated);
    }
}

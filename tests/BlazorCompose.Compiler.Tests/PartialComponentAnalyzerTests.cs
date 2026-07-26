using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class PartialComponentAnalyzerTests
{
    // Same component as GeneratorTests but without the partial modifier.
    private const string NonPartialCounterSource = """
        using BlazorCompose;
        using static BlazorCompose.Html;

        public class Counter : ComposeComponentBase
        {
            protected override View Body => Span("Count");
        }
        """;

    [Fact]
    public async Task PartialComponentAnalyzer_NonPartialComponent_ReportsBC1001AtClassIdentifier()
    {
        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(NonPartialCounterSource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BC1001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        // Verify the squiggle lands on the class name token, not the whole declaration.
        var sourceText = diagnostic.Location.SourceTree!.GetText();
        var locationText = sourceText.ToString(diagnostic.Location.SourceSpan);
        Assert.Equal("Counter", locationText);
    }

    [Fact]
    public async Task PartialComponentAnalyzer_IntermediateBaseWithoutDesignTimeExpression_ReportsNothing()
    {
        // An abstract base that supplies helpers but leaves Chrome to each concrete layout has nothing
        // generated into it, so demanding `partial` on it would be a false positive. This shape appears
        // verbatim in ComposeLayoutGeneratorTests (LabeledLayoutBase).
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public abstract class LabeledLayoutBase : ComposeLayoutBase
            {
                protected virtual string Label => "default";
            }

            public partial class Shell : LabeledLayoutBase
            {
                protected override View Chrome => Main(Body);
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "BC1001");
    }

    [Fact]
    public async Task PartialComponentAnalyzer_LeafWhoseBaseAlreadyDeclaresTheExpression_ReportsNothing()
    {
        // Special inherits ShellBase's generated RenderView, so nothing is generated into Special and
        // it needs no partial modifier.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class ShellBase : ComposeLayoutBase
            {
                protected override View Chrome => Main(Body);
            }

            public class Special : ShellBase
            {
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "BC1001");
    }

    [Fact]
    public async Task PartialComponentAnalyzer_ReAbstractedExpression_ReportsNothing()
    {
        // `abstract override` re-abstraction is legal C# and produces no getter body, so there is
        // nothing to generate and no reason to demand partial (spec F1).
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public abstract class Half : ComposeComponentBase
            {
                protected abstract override View Body { get; }
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "BC1001");
    }

    [Fact]
    public async Task PartialComponentAnalyzer_NonPartialDerivedReOverridingTheExpression_ReportsBC1001()
    {
        // The case where BC1001 is the ONLY guard (spec F16): ShellBase gets a generated RenderView, so
        // a non-partial Special that re-overrides Chrome inherits the BASE RenderView. No CS0534 fires
        // and the base chrome renders silently. Removing BC1001 here would be a silent wrong render.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public abstract partial class ShellBase : ComposeLayoutBase
            {
                protected override View Chrome => Div("base");
            }

            public class Special : ShellBase
            {
                protected override View Chrome => Div("special");
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "BC1001");
        var sourceText = diagnostic.Location.SourceTree!.GetText();
        Assert.Equal("Special", sourceText.ToString(diagnostic.Location.SourceSpan));
    }
}

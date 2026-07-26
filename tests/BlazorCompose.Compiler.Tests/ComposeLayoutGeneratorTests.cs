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

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "BC1001");
        // The message must name the real base (ComposeLayoutBase), not the ComposeComponentBase literal
        // that a naive fix could leave baked into the message format.
        Assert.Contains("ComposeLayoutBase", diagnostic.GetMessage());
        Assert.DoesNotContain("ComposeComponentBase", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Generator_NonPartialComposeComponent_BC1001MessageNamesComposeComponentBase()
    {
        // Companion to the layout case above: a ComposeComponentBase subclass must still get its own
        // (correct) base name in the message, not a leftover "ComposeLayoutBase" from shared code.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public class Counter : ComposeComponentBase
            {
                protected override View Body => Span("Count");
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<PartialComponentAnalyzer>(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "BC1001");
        Assert.Contains("ComposeComponentBase", diagnostic.GetMessage());
        Assert.DoesNotContain("ComposeLayoutBase", diagnostic.GetMessage());
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

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "BC3001");
        // The message must name the real expression (Chrome), not the Body literal.
        Assert.Contains("Chrome", diagnostic.GetMessage());
        Assert.DoesNotContain("Body", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Generator_MutationInsideComponentBody_BC3001MessageNamesBody()
    {
        // Companion to the Chrome case above: a ComposeComponentBase mutation must still say "Body".
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Counter : ComposeComponentBase
            {
                private int _n;
                protected override View Body => Span($"{_n++}");
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "BC3001");
        Assert.Contains("Body", diagnostic.GetMessage());
        Assert.DoesNotContain("Chrome", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Generator_MutationOutsideChrome_DoesNotReportBC3001()
    {
        // Regression guard for the semantic lookup replacing the "Body" literal: a mutation inside an
        // *override* property that is NOT the layout's design-time expression (Chrome) must not be
        // attributed to it. This exercises the exact branch that changed — TryGetDesignTimeExpressionOwnerType
        // now compares the property's name against ComposeComponentBaseFacts.FindDesignTimeExpressionName(type)
        // instead of the literal "Body" — so an unrelated override sharing neither name must still be
        // rejected. An intermediate class supplies the overridable "Label" property, since neither Compose
        // base declares any other overridable member for a leaf class to shadow.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public abstract class LabeledLayoutBase : ComposeLayoutBase
            {
                public virtual string Label => "default";
            }

            public partial class Shell : LabeledLayoutBase
            {
                private int _n;
                protected override View Chrome => Main(Span("static"));

                public override string Label => (_n++).ToString();
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "BC3001");
    }
}

using System.Globalization;
using System.Threading.Tasks;
using BlazorCodeFirst.Compiler.Diagnostics;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ChromeLayoutGeneratorTests
{
    [Fact]
    public void Generator_ChromeLayout_EmitsRenderViewFromChrome()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Shell : ChromeLayoutBase
            {
                protected override View Chrome => Main[Body];
            }
            """;

        var generated = Assert.Single(CompilationTestHost.RunGenerator(source).GeneratedSources).SourceText.ToString();

        Assert.Contains("protected override void RenderView(", generated);
        Assert.Contains("__builder.OpenElement(0, \"main\");", generated);
        Assert.Contains("__builder.AddContent(1, Body);", generated);
    }

    [Fact]
    public void Generator_NonPartialChromeLayout_ReportsBCF1001()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public class Shell : ChromeLayoutBase
            {
                protected override View Chrome => Main[Body];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1001");
        // The message must name the real base (ChromeLayoutBase), not the BodyComponentBase literal
        // that a naive fix could leave baked into the message format.
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("ChromeLayoutBase", message, StringComparison.Ordinal);
        Assert.DoesNotContain("BodyComponentBase", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_NonPartialBodyComponent_BCF1001MessageNamesBodyComponentBase()
    {
        // Companion to the layout case above: a BodyComponentBase subclass must still get its own
        // (correct) base name in the message, not a leftover "ChromeLayoutBase" from shared code.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public class Counter : BodyComponentBase
            {
                protected override View Body => Span["Count"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1001");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("BodyComponentBase", message, StringComparison.Ordinal);
        Assert.DoesNotContain("ChromeLayoutBase", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generator_MutationInsideChrome_ReportsBCF3001()
    {
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Shell : ChromeLayoutBase
            {
                private int _n;
                protected override View Chrome => Main[Span[$"{_n++}"]];
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "BCF3001");
        // The message must name the real expression (Chrome), not the Body literal.
        Assert.Contains("Chrome", diagnostic.GetMessage());
        Assert.DoesNotContain("Body", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Generator_MutationInsideComponentBody_BCF3001MessageNamesBody()
    {
        // Companion to the Chrome case above: a BodyComponentBase mutation must still say "Body".
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private int _n;
                protected override View Body => Span[$"{_n++}"];
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        var diagnostic = Assert.Single(diagnostics, d => d.Id == "BCF3001");
        Assert.Contains("Body", diagnostic.GetMessage());
        Assert.DoesNotContain("Chrome", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Generator_MutationOutsideChrome_DoesNotReportBCF3001()
    {
        // Regression guard for the semantic lookup replacing the "Body" literal: a mutation inside an
        // *override* property that is NOT the layout's design-time expression (Chrome) must not be
        // attributed to it. This exercises the exact branch that changed — TryGetDesignTimeExpressionOwnerType
        // now compares the property's name against DesignTimeBaseFacts.FindDesignTimeExpressionName(type)
        // instead of the literal "Body" — so an unrelated override sharing neither name must still be
        // rejected. An intermediate class supplies the overridable "Label" property, since neither Compose
        // base declares any other overridable member for a leaf class to shadow.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public abstract class LabeledLayoutBase : ChromeLayoutBase
            {
                public virtual string Label => "default";
            }

            public partial class Shell : LabeledLayoutBase
            {
                private int _n;
                protected override View Chrome => Main[Span["static"]];

                public override string Label => (_n++).ToString();
            }
            """;

        var diagnostics = await CompilationTestHost.RunAnalyzerAsync<RenderMutationAnalyzer>(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "BCF3001");
    }

    [Fact]
    public void Generator_UntranslatableChrome_BCF1003MessageNamesChrome()
    {
        // BCF1003 hardcoded the word "Body", which is a false statement for a layout: what failed to
        // translate is Chrome. GetView() is a plain View-returning method, which is the Opaque path and
        // is not implemented, so the template comes out null and BCF1003 fires.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Shell : ChromeLayoutBase
            {
                private View GetView() => Span["x"];

                protected override View Chrome => Main[GetView()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1003");
        Assert.Contains("Chrome", diagnostic.GetMessage());
        Assert.DoesNotContain("Body", diagnostic.GetMessage());
    }

    [Fact]
    public void Generator_UntranslatableBody_BCF1003MessageNamesBody()
    {
        // Companion: a component must still say Body.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private View GetView() => Span["x"];

                protected override View Body => Div[GetView()];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1003");
        Assert.Contains("Body", diagnostic.GetMessage());
        Assert.DoesNotContain("Chrome", diagnostic.GetMessage());
    }

    [Fact]
    public void Generator_GenericLayout_EmitsTypeParametersInTheClassHeader()
    {
        // Generic layouts are supported. The generated part must repeat the type-parameter list so it
        // joins the same generic type. This is the layout counterpart to the generic component test
        // Generator_GenericComponent_EmitsTypeParametersInTheClassHeader; both pin that the Chrome
        // and Body paths carry type parameters through the pipeline and into the emitted header.
        const string source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class GenLayout<TItem> : ChromeLayoutBase
            {
                protected override View Chrome => Main[Body];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains("partial class GenLayout<TItem>", generated, StringComparison.Ordinal);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

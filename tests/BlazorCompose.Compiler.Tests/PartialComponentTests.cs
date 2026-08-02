using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// BCF1001 must be reported by the generator, not by an analyzer.  Its trigger condition — a class that
/// declares the design-time expression without <c>partial</c>, so no <c>RenderView</c> is emitted into it
/// — always produces CS0534, a declaration-level error, and csc does not run the analyzer driver on a
/// compilation that has one.  An analyzer-reported BCF1001 is therefore unreachable in a real build: the
/// condition it diagnoses is the condition that suppresses it (issue #76).  The generator driver has no
/// such gate, which is why BCF1003 and BCF1005 survive the same CS0534.
/// </summary>
public sealed class PartialComponentTests
{
    // Same component as GeneratorTests but without the partial modifier.
    private const string NonPartialCounterSource = """
        using BlazorCompose;
        using static BlazorCompose.Html;

        public class Counter : ComposeComponentBase
        {
            protected override View Body => Span["Count"];
        }
        """;

    [Fact]
    public void Generator_NonPartialComponent_ReportsBCF1001AtClassIdentifier()
    {
        var result = CompilationTestHost.RunGenerator(NonPartialCounterSource);

        Assert.Empty(result.GeneratedSources);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1001");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("Counter", message, StringComparison.Ordinal);
        Assert.Contains("Body", message, StringComparison.Ordinal);
        Assert.Contains("ComposeComponentBase", message, StringComparison.Ordinal);

        // Verify the squiggle lands on the class name token, not the whole declaration. The span is
        // resolved against the original source because a generator diagnostic carries an ExternalFile
        // Location (DiagnosticInfo.ToDiagnostic) whose SourceTree is null.
        Assert.Equal(
            "Counter",
            SourceText.From(NonPartialCounterSource).ToString(diagnostic.Location.SourceSpan));

        // BCF1003 is about constructs inside the expression. The expression here is fine; the missing
        // partial modifier is the whole problem, so conflating the two would misdirect the author.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void Generator_IntermediateBaseWithoutDesignTimeExpression_ReportsNoBCF1001()
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
                protected override View Chrome => Main[Body];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1001");
    }

    [Fact]
    public void Generator_LeafWhoseBaseAlreadyDeclaresTheExpression_ReportsNoBCF1001()
    {
        // Special inherits ShellBase's generated RenderView, so nothing is generated into Special and
        // it needs no partial modifier.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class ShellBase : ComposeLayoutBase
            {
                protected override View Chrome => Main[Body];
            }

            public class Special : ShellBase
            {
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1001");
    }

    [Fact]
    public void Generator_ReAbstractedExpression_ReportsNoBCF1001()
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

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1001");
    }

    [Fact]
    public void Generator_NonPartialDerivedReOverridingTheExpression_ReportsBCF1001()
    {
        // The case where BCF1001 is the ONLY guard (spec F16): ShellBase gets a generated RenderView, so
        // a non-partial Special that re-overrides Chrome inherits the BASE RenderView. No CS0534 fires
        // and the base chrome renders silently. Removing BCF1001 here would be a silent wrong render.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public abstract partial class ShellBase : ComposeLayoutBase
            {
                protected override View Chrome => Div["base"];
            }

            public class Special : ShellBase
            {
                protected override View Chrome => Div["special"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF1001");
        Assert.Equal("Special", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));

        // ShellBase is partial and still gets its RenderView; only the derived class is reported.
        Assert.Single(result.GeneratedSources);
    }

    [Fact]
    public void Generator_NonPartialWithHandWrittenRenderView_ReportsNoBCF1001()
    {
        // Hand-writing RenderView is the documented escape hatch, and it makes `partial` pointless: the
        // generator contributes nothing to a class that already has a RenderView, whether or not it is
        // partial. Demanding the modifier here would be the same false positive as demanding it of a
        // class that declares no design-time expression. The analyzer that used to own BCF1001 could not
        // make this distinction because it never looked at RenderView.
        const string source = """
            using BlazorCompose;
            using Microsoft.AspNetCore.Components.Rendering;
            using static BlazorCompose.Html;

            public class Counter : ComposeComponentBase
            {
                protected override View Body => Span["Count"];

                protected override void RenderView(RenderTreeBuilder builder)
                {
                    builder.AddContent(0, "hand written");
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1001");
        Assert.Empty(result.GeneratedSources);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Generator_NestedNonPartialComponent_ReportsBCF1005NotBCF1001()
    {
        // Both modifiers are wrong here, but only one remedy exists: adding `partial` to a nested class
        // still generates nothing, so BCF1001's "add the partial modifier" would be a false lead. BCF1005
        // names the condition the author has to remove.
        const string source = """
            using BlazorCompose;
            using static BlazorCompose.Html;

            public partial class Outer
            {
                public class Counter : ComposeComponentBase
                {
                    protected override View Body => Span["Count"];
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Single(result.Diagnostics, d => d.Id == "BCF1005");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1001");
    }
}

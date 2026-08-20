using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// BCF3016: children on a void element. The rule is a unary predicate on the element tag, so what is
/// asserted here is that both surface paths reduce to the same tag before it is applied, and that the
/// predicate's boundary is where <c>DESIGN.md</c> §4.1 says it is.
/// </summary>
/// <remarks>
/// The delivery layer (<c>tests/diagnostic-fixtures</c>) carries only the curated spelling, because
/// <c>DiagnosticDeliveryTests</c> requires exactly one report per id per fixture project. Every other
/// shape of the rule is asserted here.
/// </remarks>
public sealed class VoidElementDiagnosticTests
{
    private static ImmutableArray<Diagnostic> Run(string body) =>
        CompilationTestHost.RunGenerator($$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                private readonly string[] _items = ["a", "b"];

                protected override View Body => {{body}};
            }
            """).Diagnostics;

    private static GeneratorRunResult RunResult(string body) =>
        CompilationTestHost.RunGenerator($$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace T;

            public partial class Host : BodyComponentBase
            {
                protected override View Body => {{body}};
            }
            """);

    [Fact]
    public void CuratedVoidHelper_WithChildren_ReportsBCF3016()
    {
        var diagnostics = Run("""Img["child"]""");

        var reported = Assert.Single(diagnostics.Where(static d => d.Id == "BCF3016"));
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);

        // The tag, not the helper name: the message has to name what the author will look up in the HTML
        // standard, and it is the same string on both surface paths.
        Assert.Contains("'img'", reported.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void ElementWithAVoidTag_WithChildren_ReportsTheSameDiagnostic()
    {
        // The point of a single tag table. Were there two, this and the test above could pass while
        // disagreeing about, say, `source`, and nothing would notice.
        var diagnostics = Run("""Element("img")["child"]""");

        var reported = Assert.Single(diagnostics.Where(static d => d.Id == "BCF3016"));
        Assert.Contains("'img'", reported.GetMessage(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void EveryVoidTag_ReportsThroughElement()
    {
        // Enumerated rather than sampled: `base`, `link` and `meta` have no curated helper and are
        // reachable only this way, and a set that silently lost one would otherwise be caught by nothing
        // but KnownSymbolsSyncTests' transcription comparison.
        foreach (var tag in KnownSymbols.VoidTags)
        {
            var diagnostics = Run($$"""Element("{{tag}}")["child"]""");

            Assert.Contains(
                diagnostics,
                d => d.Id == "BCF3016" && d.GetMessage().Contains($"'{tag}'", System.StringComparison.Ordinal));
        }
    }

    [Fact]
    public void DecoratedVoidElement_WithChildren_ReportsBCF3016()
    {
        // The receiver is a decoration chain rather than the bare helper, so the tag arrives on an
        // ElementTemplateNode built by the decoration arm. Same check, one path further out.
        var diagnostics = Run("""Img.Src("/a.png").Alt("a")["child"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3016");
    }

    [Fact]
    public void VoidElement_WithNoChildren_IsAccepted()
    {
        // The shape the whole surface is built around. A rule keyed on the tag alone would reject this too
        // if it were applied to the childless form, which is the receiver of every bracketed one.
        var result = RunResult("""Div[Img.Src("/a.png").Alt("a"), Br, Hr]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void NonVoidElement_WithChildren_IsAccepted()
    {
        var result = RunResult("""Div[Span["x"], Element("my-widget")["y"]]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void UnknownTag_WithChildren_StaysSilent()
    {
        // Custom elements and unknown tags are outside the rule by design (DESIGN.md §4.1): there is no
        // standard to read their content model out of, and a name-shaped guess would be the invented
        // vocabulary the surface exists to avoid.
        var result = RunResult("""Element("img-viewer")["child"]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void UppercaseElementTag_StaysSilent()
    {
        // Element emits its tag as written, so Element("IMG") renders <IMG> and is a different string from
        // the standard's `img`. Pinned because an ordinal comparison here is a choice, not an oversight:
        // matching case-insensitively would claim a tag this surface never produces.
        var result = RunResult("""Element("IMG")["child"]""");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void InertChildOnAVoidElement_StillReportsBCF3016()
    {
        // Img[Fragment()] emits a correct <img />, and reporting it anyway is the decided position (#131):
        // the rule is "the children indexer was used", so it does not depend on what a child evaluates to.
        var diagnostics = Run("""Img[Fragment()]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3016");
    }

    [Fact]
    public void VoidElementNestedInAnotherElement_ReportsBCF3016()
    {
        var diagnostics = Run("""Div[P["ok"], Br["child"]]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3016");
    }

    [Fact]
    public void BCF3016_SuppressesTheGenericBCF1003()
    {
        // Translation fails, so the un-emitted RenderView raises CS0534 in the author's compilation. What
        // must not also appear is BCF1003's "not statically analyzable", which explains nothing here.
        var diagnostics = Run("""Img["child"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3016");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF1003");
    }

    [Fact]
    public void UntranslatableChildOnAVoidElement_ReportsBCF1003AndNotBCF3016()
    {
        // The ordering the check's position encodes: a child list the generator cannot see through is the
        // more basic complaint, and BCF1003 points at it. Widening BCF3016 to fire first would blame the
        // brackets for a problem that survives removing the element.
        var diagnostics = Run("""Img[_items]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3016");
    }

    [Fact]
    public void VoidElementAsAForEachContentRoot_ReportsBCF3016AndNotBCF3003()
    {
        // A void element opens a single frame, so it is keyable and BCF3003 has nothing to say. The two
        // diagnostics are about different things and this is where that could have gone wrong: the void
        // check rejects the element, and a rejected element could have been mistaken for a missing frame.
        var diagnostics = Run("""ForEach(_items, item => item, item => Img[item])""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3016");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3003");
    }

    [Fact]
    public void NonConstantElementTag_WithChildren_ReportsBCF3009Only()
    {
        // BCF3009 rejects the tag before there is a string to test, so BCF3016 never sees this shape. Its
        // own report would have to invent a tag to name.
        var diagnostics = Run("""Element(string.Join("", "i", "mg"))["child"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BCF3009");
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BCF3016");
    }
}

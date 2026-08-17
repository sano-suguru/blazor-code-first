using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The rules that fence off <c>Component&lt;T&gt;().Template</c>: BCF3022 for a contextual content
/// argument the generator cannot statically sequence, and BCF3007 for a parameter bound twice through
/// the template channel.
/// </summary>
public sealed class ComponentTemplateDiagnosticTests
{
    private const string TemplateTargetSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class TemplateTarget : ComponentBase
        {
            [Parameter] public RenderFragment<int>? RowTemplate { get; set; }
        }
        """;

    /// <summary>
    /// The source of a host whose <c>Body</c> is <paramref name="body"/> and which also declares a
    /// <c>Render</c> method group of the contextual template's shape, so a rejected method-group
    /// spelling is a real conversion rather than an unresolved name.
    /// </summary>
    private static string HostSource(string body) =>
        $$"""
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;
        namespace T;
        public partial class Host : BodyComponentBase
        {
            protected override View Body => {{body}};

            private static View Render(int value) => Span[value.ToString()];
        }
        """;

    private static GeneratorRunResult Run(string body) =>
        CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", HostSource(body)));

    [Theory]
    [InlineData("x => Span[x.ToString()]")]
    [InlineData("static x => Span[x.ToString()]")]
    [InlineData("(int x) => Span[x.ToString()]")]
    public void ContextualTemplate_InlineExpressionLambda_IsAcceptedAndCompiles(string content)
    {
        var result = Run($"Component<TemplateTarget>().Template(c => c.RowTemplate, {content})");

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3022");
        Assert.Contains(result.GeneratedSources, static s => s.HintName.Contains("Host"));
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Theory]
    [InlineData("Render")]
    [InlineData("x => { return Span[x.ToString()]; }")]
    [InlineData("delegate(int x) { return Span[x.ToString()]; }")]
    public void ContextualTemplate_NonInlineContent_ReportsBCF3022OnTheWholeArgument(string content)
    {
        var body = $"Component<TemplateTarget>().Template(c => c.RowTemplate, {content})";
        var result = Run(body);

        var reported = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BCF3022"));
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);
        Assert.Equal(
            content,
            SourceText.From(HostSource(body)).ToString(reported.Location.SourceSpan));

        // The shape is named, so the transitional "not statically analyzable" fallback stays silent.
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3015");
        Assert.DoesNotContain(result.GeneratedSources, static s => s.HintName.Contains("Host"));
    }

    [Theory]
    // The builder the fragment's frames are written against. The generated lambda takes one of its own,
    // so a body declaring that name shadows it.
    [InlineData("the builder's name", """x => Span[x is int __builder ? "a" : "b"]""")]
    // The other form an expression body binds a local through. Scanning patterns alone let `out var`
    // reach the generated scope once already (#348).
    [InlineData("the builder's name through an out designation", """x => Span[int.TryParse("1", out var __builder) ? "a" : "b"]""")]
    // A generated name the context rename does not carry: hygiene rewrites __bcf_context_<digits> and
    // nothing else, so the iteration variable of an enclosing ForEach collides as written.
    [InlineData("a generator-reserved name", """x => Span[x is int __bcf_item_0 ? "a" : "b"]""")]
    // The name hygiene does carry. Refused all the same, because one scan answers for every position that
    // transplants under the author's names and a second, narrower one would fork it (#389).
    [InlineData("the generated context name", """x => Span[x is int __bcf_context_1 ? "a" : "b"]""")]
    public void ContextualTemplate_WhenItDeclaresAReservedName_ReportsBCF3022(string shape, string content)
    {
        // The body is transplanted into the generated fragment under the author's own names, and the
        // getter's scan stops at this lambda, which left the collision to reach a generated file the
        // author cannot edit (#413).
        var result = Run($"Component<TemplateTarget>().Template(c => c.RowTemplate, {content})");

        Assert.True(
            result.Diagnostics.Any(static d => d.Id == "BCF3022"),
            $"{shape}: expected BCF3022, got [{string.Join(", ", result.Diagnostics.Select(static d => d.Id))}].");
    }

    [Fact]
    public void ContextualTemplate_WhenItDeclaresAReservedName_ReportsBCF3022OnTheWholeArgument()
    {
        // The same position the non-inline shapes are reported at: what the author rewrites is the
        // argument, and naming the declaration alone would point at a name that is legal elsewhere.
        const string content = """x => Span[x is int __builder ? "a" : "b"]""";
        var body = $"Component<TemplateTarget>().Template(c => c.RowTemplate, {content})";
        var result = Run(body);

        var reported = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BCF3022"));
        Assert.Equal(
            content,
            SourceText.From(HostSource(body)).ToString(reported.Location.SourceSpan));
    }

    [Theory]
    [InlineData("() => Span[\"x\"]")]
    [InlineData("(int x, int y) => Span[x.ToString()]")]
    public void ContextualTemplate_WrongArity_IsLeftToCSharp(string content)
    {
        // Neither shape converts to Func<TContext, View>, so C# rejects the call before this rule
        // could apply. Reporting BCF3022 on top would name a fix the author has already been given.
        //
        // CS1660 is asserted alongside BCF3022's silence because it is the reason for that silence.
        // With the contextual overload discarded on arity, the lambda is measured against the
        // context-ignoring overload's View parameter and fails to convert, and GetSymbolInfo on the
        // invocation then reports the implicit ComponentView<T>-to-View conversion rather than
        // Template. No arm of RenderExpressionAnalyzer's classifier matches a conversion operator, so
        // the contextual branch is never entered. Without pinning that, this test stays green for a
        // compilation that bound the call to Template and then declined to report, which is a
        // different behavior under the same assertion.
        var result = Run($"Component<TemplateTarget>().Template(c => c.RowTemplate, {content})");

        Assert.Contains(
            result.OutputCompilation.GetDiagnostics(),
            static d => d.Severity == DiagnosticSeverity.Error && d.Id == "CS1660");
        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF3022");
    }

    [Fact]
    public void TwoTemplates_SameParameter_ReportBCF3007WithoutNamingParam()
    {
        var result = Run(
            "Component<TemplateTarget>()"
                + ".Template(c => c.RowTemplate, x => Span[x.ToString()])"
                + ".Template(c => c.RowTemplate, x => Div[x.ToString()])");

        var reported = Assert.Single(result.Diagnostics.Where(static d => d.Id == "BCF3007"));
        Assert.Equal(DiagnosticSeverity.Error, reported.Severity);

        // The duplicate here is two .Template calls, so an instruction to remove a .Param call would
        // name something the author never wrote.
        Assert.DoesNotContain(".Param", reported.GetMessage(null));
    }

    [Fact]
    public void TemplateAndParam_SameParameter_ReportBCF3007()
    {
        // The two channels share one duplicate check, so a template and a scalar param collide too.
        var result = Run(
            "Component<TemplateTarget>()"
                + ".Template(c => c.RowTemplate, x => Span[x.ToString()])"
                + ".Param(c => c.RowTemplate, null)");

        Assert.Contains(result.Diagnostics, static d => d.Id == "BCF3007");
    }
}

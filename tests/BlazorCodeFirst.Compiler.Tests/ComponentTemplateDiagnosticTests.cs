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
    [InlineData("() => Span[\"x\"]", "CS1660")]
    [InlineData("(int x, int y) => Span[x.ToString()]", "CS1660")]
    public void ContextualTemplate_WrongArity_IsLeftToCSharp(string content, string csharpError)
    {
        // Neither shape converts to Func<TContext, View>, so C# rejects the call before this rule
        // could apply. Reporting BCF3022 on top would name a fix the author has already been given.
        var result = Run($"Component<TemplateTarget>().Template(c => c.RowTemplate, {content})");

        // The C# error is asserted alongside BCF3022's silence because it is the reason for that
        // silence. CS1660 is reported against the context-ignoring overload, whose second parameter
        // is View: the contextual overload was discarded on arity, overload resolution failed, and so
        // GetSymbolInfo yields no symbol and RenderExpressionAnalyzer returns before its contextual
        // branch. Without pinning that, this test stays green for a compilation that bound the call
        // and then declined to report, which is a different behavior under the same assertion.
        Assert.Contains(
            result.OutputCompilation.GetDiagnostics(),
            d => d.Severity == DiagnosticSeverity.Error && d.Id == csharpError);
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

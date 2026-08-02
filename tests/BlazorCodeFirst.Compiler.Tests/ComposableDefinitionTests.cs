using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using BlazorCodeFirst.Compiler;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ComposableDefinitionTests
{
    [Theory]
    [InlineData("[Composable] private View Helper() => Span[\"x\"];", "must be static")]
    [InlineData("[Composable] private static View Helper<T>() => Span[\"x\"];", "must be non-generic")]
    [InlineData("[Composable] private static View Helper() { return Span[\"x\"]; }", "must be expression-bodied")]
    [InlineData("[Composable] private static string Helper() => \"x\";", "must return BlazorCodeFirst.View")]
    [InlineData("[Composable] private static View Helper(params string[] values) => Span[values[0]];", "params parameters are unsupported")]
    [InlineData("[Composable] private static View Helper(View content) => content;", "View parameters are unsupported")]
    [InlineData("[Composable] private static View Helper(ref int value) => Span[\"x\"];", "by-reference parameters are unsupported")]
    [InlineData("[Composable] private static View Helper(out int value) => Span[\"x\"];", "by-reference parameters are unsupported")]
    [InlineData("[Composable] private static View Helper(in int value) => Span[\"x\"];", "by-reference parameters are unsupported")]
    [InlineData("[Composable] private static View Helper(ref readonly int value) => Span[\"x\"];", "by-reference parameters are unsupported")]
    public void ComposableDefinition_UnsupportedDeclaration_ReportsBCF1002(string declaration, string message)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                {{declaration}}
                protected override View Body => Span["Body"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(
            result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(message, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ComposableDefinition_ValidDefinition_DoesNotReportBCF1002()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Greeting(string name) => Span[name];

                protected override View Body => Span["Body"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
    }

    [Fact]
    public void ComposableRegistry_EntriesDiscoveredOutOfOrder_RemainsValueEqual()
    {
        var high = new ComposableDefinitionEntry("K:b", "Beta", Definition: null, DeclarationDiagnosticReported: true);
        var low = new ComposableDefinitionEntry("K:a", "Alpha", Definition: null, DeclarationDiagnosticReported: true);

        var registry = ComposableRegistry.Create([high, low]);
        var reordered = ComposableRegistry.Create([low, high]);

        Assert.Equal(registry, reordered);
        Assert.Equal(registry.GetHashCode(), reordered.GetHashCode());

        // Entries are sorted by method key so equality is discovery-order independent.
        Assert.Equal("K:a", registry.Entries[0].MethodKey);
        Assert.Equal("K:b", registry.Entries[1].MethodKey);

        Assert.True(registry.TryGet("K:a", out var found));
        Assert.Equal("Alpha", found.DisplayName);
        Assert.False(registry.TryGet("missing", out _));
    }

    [Fact]
    public void ComposableRegistry_DuplicateMethodKeys_RetainsFirstEntryOnly()
    {
        var first = new ComposableDefinitionEntry("K", "First", Definition: null, DeclarationDiagnosticReported: true);
        var duplicate = new ComposableDefinitionEntry("K", "Second", Definition: null, DeclarationDiagnosticReported: true);

        var registry = ComposableRegistry.Create([first, duplicate]);

        var entry = Assert.Single(registry.Entries);
        Assert.Equal("First", entry.DisplayName);
    }

    [Fact]
    public void ComposableCallTemplate_OmittedOptionalArguments_SortAfterSuppliedArgumentsInParameterOrder()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Target(string a, int b = 1, int c = 2) => Span[a];

                [Composable]
                private static View Caller() => Target("supplied");

                protected override View Body => Span["Body"];
            }
            """;

        var call = (ComposableCallTemplateNode)AnalyzeBody(source, "Caller")!;
        var arguments = call.Arguments;

        Assert.Equal(3, arguments.Length);

        var supplied = Assert.Single(arguments, static a => !a.IsImplicitDefault);
        var implicitDefaults = arguments.Where(static a => a.IsImplicitDefault).ToArray();

        // Every implicit default sorts strictly after the single supplied argument.
        Assert.All(implicitDefaults, d => Assert.True(d.SourceOrder > supplied.SourceOrder));

        // Implicit defaults remain in parameter order (b before c), with no overflow wrap-around.
        var defaultsInParameterOrder = implicitDefaults.OrderBy(static a => a.ParameterOrdinal).ToArray();
        for (var index = 1; index < defaultsInParameterOrder.Length; index++)
        {
            Assert.True(
                defaultsInParameterOrder[index].SourceOrder > defaultsInParameterOrder[index - 1].SourceOrder);
        }

        // Sorting purely by SourceOrder reproduces the declared parameter order (0, 1, 2).
        var bySourceOrder = arguments.OrderBy(static a => a.SourceOrder)
            .Select(static a => a.ParameterOrdinal)
            .ToArray();
        var expectedOrder = new[] { 0, 1, 2 };
        Assert.Equal(expectedOrder, bySourceOrder);
    }

    [Fact]
    public void ComposableDefinition_ExpressionBodyReferencesEnclosingLocal_ReportsSingleBCF1002()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Helper(string s) => Div[
                    Span[int.TryParse(s, out var parsed) ? s : s],
                    Span[parsed.ToString()]];

                protected override View Body => Span["Body"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");
        Assert.Contains("parsed", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ComposableDefinition_ExpressionBodyUsesSelfContainedLocal_DoesNotReportBCF1002()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Helper(string s) =>
                    Span[int.TryParse(s, out var parsed) ? parsed.ToString() : "0"];

                protected override View Body => Span["Body"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
    }

    [Fact]
    public void ExpressionTemplate_ParameterExpressionContainsNameof_CollapsesNameofAndSubstitutesHole()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [Composable]
                private static View Greeting(string name) => Span[nameof(name) + name];

                protected override View Body => Span["Body"];
            }
            """;

        var compilation = CompilationTestHost.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);

        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(static m => m.Identifier.Text == "Greeting");
        var methodSymbol = model.GetDeclaredSymbol(method)!;

        var elementAccess = (ElementAccessExpressionSyntax)method.ExpressionBody!.Expression;
        var argument = elementAccess.ArgumentList.Arguments[0].Expression;

        var knownSymbols = KnownSymbols.TryCreate(compilation)!;
        var ordinals = methodSymbol.Parameters.ToImmutableDictionary(
            static p => (ISymbol)p,
            static p => p.Ordinal,
            SymbolEqualityComparer.Default);

        var context = new ComposableBodyContext(
            model,
            methodSymbol.ContainingType,
            methodSymbol.Name,
            knownSymbols,
            ordinals,
            default);

        var template = ExpressionTemplateFactory.Create(argument, context);
        var code = template.Substitute(["__p0"]).ToCode();

        // nameof(name) depends on the parameter, so it collapses to its compile-time constant string;
        // the bare 'name' becomes the substituted hole.
        Assert.Equal("\"name\" + __p0", code);
    }

    private static RenderTemplateNode? AnalyzeBody(string source, string methodName)
    {
        var compilation = CompilationTestHost.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);

        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.Text == methodName);
        var methodSymbol = model.GetDeclaredSymbol(method)!;

        var knownSymbols = KnownSymbols.TryCreate(compilation)!;
        var ordinals = methodSymbol.Parameters.ToImmutableDictionary(
            static p => (ISymbol)p,
            static p => p.Ordinal,
            SymbolEqualityComparer.Default);

        var context = new ComposableBodyContext(
            model,
            methodSymbol.ContainingType,
            methodSymbol.Name,
            knownSymbols,
            ordinals,
            default);

        return RenderExpressionAnalyzer.Analyze(method.ExpressionBody!.Expression, context);
    }
}

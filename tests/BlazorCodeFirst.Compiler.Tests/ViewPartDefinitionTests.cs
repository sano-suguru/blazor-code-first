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

public sealed class ViewPartDefinitionTests
{
    [Theory]
    [InlineData("[ViewPart] private View Helper() => Span[\"x\"];", "must be static")]
    [InlineData("[ViewPart] private static View Helper<T>() => Span[\"x\"];", "must be non-generic")]
    // A block body is accepted since #336, in the one shape the other Transplantable positions take.
    // What is refused is a block outside it: a second return needs a sequence space of its own.
    // ViewPartStatementBodyTests covers the accepted shape and the rest of the refused ones.
    [InlineData(
        "[ViewPart] private static View Helper() { if (true) return Span[\"a\"]; return Span[\"x\"]; }",
        "body must be an expression, or a block")]
    [InlineData("[ViewPart] private static string Helper() => \"x\";", "must return BlazorCodeFirst.View")]
    [InlineData("[ViewPart] private static View Helper(params string[] values) => Span[values[0]];", "params parameters are unsupported")]
    // A View parameter is a content slot now (#34) and needs the SlotView return type that says so;
    // ViewPartContentDiagnosticTests covers the surface it belongs to.
    [InlineData("[ViewPart] private static View Helper(View content) => content;", "View parameters are content slots")]
    [InlineData("[ViewPart] private static View Helper(ref int value) => Span[\"x\"];", "by-reference parameters are unsupported")]
    [InlineData("[ViewPart] private static View Helper(out int value) => Span[\"x\"];", "by-reference parameters are unsupported")]
    [InlineData("[ViewPart] private static View Helper(in int value) => Span[\"x\"];", "by-reference parameters are unsupported")]
    [InlineData("[ViewPart] private static View Helper(ref readonly int value) => Span[\"x\"];", "by-reference parameters are unsupported")]
    public void ViewPartDefinition_UnsupportedDeclaration_ReportsBCF1002(string declaration, string message)
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

        AssertSingleBCF1002(source, message);
    }

    /// <summary>
    /// An extension member is not a view part (<c>DESIGN.md</c> §4.3, #203), and which spelling declared
    /// it does not change the answer: the classic <c>this</c> parameter and both members of a C# 14
    /// <c>extension</c> block report the one reason that is true of them. A separate theory because an
    /// extension member needs a <c>static</c> containing class, which the rows above cannot share (one of
    /// them declares an instance method). The static extension member is the row the second half of the
    /// predicate exists for — it is static and non-generic and would otherwise pass every remaining test.
    /// </summary>
    [Theory]
    [InlineData("[ViewPart] public static View Label(this string value) => Span[value];")]
    [InlineData("extension(string value) { [ViewPart] public View Label() => Span[value]; }")]
    [InlineData("extension(string value) { [ViewPart] public static View Make() => Span[\"x\"]; }")]
    public void ViewPartDefinition_ExtensionMember_ReportsBCF1002(string declaration)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public static class Helpers
            {
                {{declaration}}
            }

            public partial class Counter : BodyComponentBase
            {
                protected override View Body => Span["Body"];
            }
            """;

        AssertSingleBCF1002(source, "must not be an extension member");
    }

    private static void AssertSingleBCF1002(string source, string message)
    {
        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, static d => d.Id == "BCF1002");

        Assert.Contains(message, diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ViewPartDefinition_ValidDefinition_DoesNotReportBCF1002()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [ViewPart]
                private static View Greeting(string name) => Span[name];

                protected override View Body => Span["Body"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Id == "BCF1002");
    }

    [Fact]
    public void ViewPartRegistry_EntriesDiscoveredOutOfOrder_RemainsValueEqual()
    {
        var high = new ViewPartDefinitionEntry("K:b", "Beta", Definition: null, DeclarationDiagnosticReported: true);
        var low = new ViewPartDefinitionEntry("K:a", "Alpha", Definition: null, DeclarationDiagnosticReported: true);

        var registry = ViewPartRegistry.Create([high, low]);
        var reordered = ViewPartRegistry.Create([low, high]);

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
    public void ViewPartRegistry_DuplicateMethodKeys_RetainsFirstEntryOnly()
    {
        var first = new ViewPartDefinitionEntry("K", "First", Definition: null, DeclarationDiagnosticReported: true);
        var duplicate = new ViewPartDefinitionEntry("K", "Second", Definition: null, DeclarationDiagnosticReported: true);

        var registry = ViewPartRegistry.Create([first, duplicate]);

        var entry = Assert.Single(registry.Entries);
        Assert.Equal("First", entry.DisplayName);
    }

    [Fact]
    public void ViewPartCallTemplate_OmittedOptionalArguments_SortAfterSuppliedArgumentsInParameterOrder()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [ViewPart]
                private static View Target(string a, int b = 1, int c = 2) => Span[a];

                [ViewPart]
                private static View Caller() => Target("supplied");

                protected override View Body => Span["Body"];
            }
            """;

        var call = (ViewPartCallTemplateNode)AnalyzeBody(source, "Caller")!;
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
    public void ViewPartDefinition_ExpressionBodyReferencesEnclosingLocal_ReportsSingleBCF1002()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [ViewPart]
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
    public void ViewPartDefinition_ExpressionBodyUsesSelfContainedLocal_DoesNotReportBCF1002()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                [ViewPart]
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
                [ViewPart]
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

        var context = new ViewPartBodyContext(
            model,
            methodSymbol.ContainingType,
            methodSymbol.Name,
            knownSymbols,
            ordinals,
            default);

        var template = ExpressionTemplateFactory.Create(argument, context);
        var code = template.Substitute([new SubstitutedArgument("__p0", Constant: null)]).ToCode();

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

        var context = new ViewPartBodyContext(
            model,
            methodSymbol.ContainingType,
            methodSymbol.Name,
            knownSymbols,
            ordinals,
            default);

        return RenderExpressionAnalyzer.Analyze(method.ExpressionBody!.Expression, context);
    }
}

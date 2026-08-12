using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ViewPartBodyContextTests
{
    [Fact]
    public void PushRenderVariable_WithZeroBaseParameters_AssignsOrdinalZeroThenOne()
    {
        var compilation = CompilationTestHost.CreateCompilation(
            "class C { void M(object a, object b, object c, object d) { } }");
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var symbols = method.ParameterList.Parameters
            .Select(p => (ISymbol)model.GetDeclaredSymbol(p)!)
            .ToArray();
        var containing = (INamedTypeSymbol)model.GetDeclaredSymbol(
            tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single())!;

        var context = new ViewPartBodyContext(
            model, containing, "Body",
            KnownSymbols.TryCreate(compilation)!,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            CancellationToken.None);

        var outer = context.PushRenderVariable(symbols[0], symbols[1]);
        var inner = context.PushRenderVariable(symbols[2], symbols[3]);

        Assert.Equal(0, outer);
        Assert.Equal(1, inner);
        Assert.True(context.ResolveHole(symbols[0], out var o0) == BodyHoleKind.Value && o0 == 0);
        Assert.True(context.ResolveHole(symbols[3], out var o3) == BodyHoleKind.Value && o3 == 1);

        context.PopRenderVariable(symbols[2], symbols[3]);
        Assert.Equal(BodyHoleKind.None, context.ResolveHole(symbols[3], out _));
        Assert.Equal(1, context.PushRenderVariable(symbols[2], symbols[3]));
    }

    [Fact]
    public void Analyze_ContextualTemplateBodyFails_PopsTheScopedRenderVariable()
    {
        const string source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;

            public sealed class Target : ComponentBase
            {
                [Parameter] public RenderFragment<int>? RowTemplate { get; set; }
            }

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<Target>().Template(c => c.RowTemplate, context => _stored);

                // A stored View. A View-returning call would classify now — BCF3030 or Opaque — and this
                // test needs a template body that still fails.
                private readonly View _stored;
            }
            """;
        var compilation = CompilationTestHost.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var host = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(static declaration => declaration.Identifier.ValueText == "Host");
        var body = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Single(static property => property.Identifier.ValueText == "Body");
        var contextParameter = body.DescendantNodes().OfType<ParameterSyntax>()
            .Single(static parameter => parameter.Identifier.ValueText == "context");
        var contextParameterSymbol = model.GetDeclaredSymbol(contextParameter)!;
        var context = new ViewPartBodyContext(
            model,
            (INamedTypeSymbol)model.GetDeclaredSymbol(host)!,
            "Body",
            KnownSymbols.TryCreate(compilation)!,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            CancellationToken.None);

        Assert.Null(RenderExpressionAnalyzer.Analyze(body.ExpressionBody!.Expression, context));
        Assert.Equal(BodyHoleKind.None, context.ResolveHole(contextParameterSymbol, out _));
    }
}

using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ComposableBodyContextTests
{
    [Fact]
    public void PushIterationVariable_WithZeroBaseParameters_AssignsOrdinalZeroThenOne()
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

        var context = new ComposableBodyContext(
            model, containing, "Body",
            KnownSymbols.TryCreate(compilation)!,
            ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default),
            CancellationToken.None);

        var outer = context.PushIterationVariable(symbols[0], symbols[1]);
        var inner = context.PushIterationVariable(symbols[2], symbols[3]);

        Assert.Equal(0, outer);
        Assert.Equal(1, inner);
        Assert.True(context.TryGetParameterOrdinal(symbols[0], out var o0) && o0 == 0);
        Assert.True(context.TryGetParameterOrdinal(symbols[3], out var o3) && o3 == 1);

        context.PopIterationVariable(symbols[2], symbols[3]);
        Assert.False(context.TryGetParameterOrdinal(symbols[3], out _));
        Assert.Equal(1, context.PushIterationVariable(symbols[2], symbols[3]));
    }
}

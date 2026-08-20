using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Resolves one named call out of a probe source in both of the spellings the parameter-role helpers have
/// to agree across, for the tests that hold those helpers to it.
/// </summary>
/// <remarks>
/// The mechanics live here rather than once per test class because they encode a fact about Roslyn, not
/// about any one helper: <c>GetSymbolInfo</c> answers with the <em>reduced</em> method for a fluent
/// extension call and the unreduced one for the static spelling, while an operation's
/// <c>IArgumentOperation.Parameter</c> always comes from the unreduced <c>TargetMethod</c>. Every test that
/// checks a parameter role across spellings needs both halves, and two transcriptions of that pairing would
/// be the same duplication the helpers under test exist to end (#206, #221).
/// </remarks>
internal static class SurfaceInvocationProbe
{
    /// <summary>
    /// The first invocation in <paramref name="source"/> naming a method called
    /// <paramref name="methodName"/>, as its resolved symbol and its operation.
    /// </summary>
    internal static (IMethodSymbol Method, IInvocationOperation Operation) Resolve(
        string source, string methodName)
    {
        var compilation = CompilationTestHost.CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);

        var invocation = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(node => model.GetSymbolInfo(node).Symbol is IMethodSymbol method
                && method.Name == methodName);

        var method = (IMethodSymbol)model.GetSymbolInfo(invocation).Symbol!;
        var operation = (IInvocationOperation)model.GetOperation(invocation)!;
        return (method, operation);
    }

    /// <summary>
    /// The parameter the argument whose source text is <paramref name="argumentText"/> binds to, read off
    /// <see cref="IArgumentOperation.Parameter"/> as <c>RenderMutationAnalyzer</c> reads it.
    /// </summary>
    /// <remarks>
    /// The argument is located by its text because a test names it that way; the analyzer arrives at the
    /// same operation by walking up from the anonymous function instead (#216).
    /// </remarks>
    internal static IParameterSymbol BoundParameter(IInvocationOperation operation, string argumentText)
    {
        var argument = operation.Arguments.Single(a => a.Syntax.ToString() == argumentText);
        Assert.NotNull(argument.Parameter);
        return argument.Parameter!;
    }
}

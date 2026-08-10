using System.Linq;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds <see cref="KnownSymbols.ReceiverOffset"/> and <see cref="KnownSymbols.ArgumentIndex"/> against
/// every spelling Roslyn hands a method out in, which is the question six places used to derive under two
/// conventions (#211).
/// </summary>
/// <remarks>
/// <para>
/// The divergence was load-bearing and recorded at one site out of six: four normalized through
/// <c>ReducedFrom</c> and then tested <c>IsExtensionMethod</c>, while <c>BindParameters</c> refused to
/// normalize because <c>ReducedFrom</c> answers the generic definition and would have made a bound value's
/// type an unsubstituted type parameter. One rule answers both, and this is what says so: the offset is
/// asked of whichever spelling arrived, and every spelling but the unreduced classic extension method
/// already excludes its receiver.
/// </para>
/// <para>
/// The C# 14 extension-block case is the one the issue names as untested. Roslyn answers
/// <see cref="IMethodSymbol.IsExtensionMethod"/> with <see langword="false"/> for that declaration form and
/// hangs the receiver off the containing extension block instead (#203), so the offset is 0 — which is
/// right, because the parameter list Roslyn reports for it already excludes the receiver. Every one of the
/// six computed that correctly by accident; here it is on purpose.
/// </para>
/// </remarks>
public sealed class ReceiverOffsetTests
{
    private const string Source = """
        public static class ClassicExtensions
        {
            public static string Twice(this string value, int times) => value;
        }

        public static class BlockExtensions
        {
            extension(string value)
            {
                public string Thrice(int times) => value;
            }
        }

        public class Instance
        {
            public string Plain(int times) => "";
        }

        public class Host
        {
            public string Fluent => "a".Twice(2);
            public string Static => ClassicExtensions.Twice("a", 2);
            public string Block => "a".Thrice(3);
            public string Method => new Instance().Plain(1);
        }
        """;

    private static Compilation Compilation { get; } = CompilationTestHost.CreateCompilation(Source);

    /// <summary>The method the getter named <paramref name="property"/> calls, in the spelling
    /// <c>GetSymbolInfo</c> answers with: reduced for a fluent extension call, unreduced for the static
    /// one.</summary>
    private static IMethodSymbol Resolve(string property)
    {
        var tree = Compilation.SyntaxTrees.First();
        var model = Compilation.GetSemanticModel(tree);

        var getter = tree.GetRoot()
            .DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .First(node => node.Identifier.ValueText == property);

        var invocation = getter.DescendantNodes().OfType<InvocationExpressionSyntax>().Last();

        var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        Assert.NotNull(method);
        return method!;
    }

    /// <summary>
    /// A classic <c>this</c> extension method written fluently: <c>GetSymbolInfo</c> answers the reduced
    /// method, whose parameter list already excludes the receiver, so nothing is skipped.
    /// </summary>
    [Fact]
    public void AReducedExtensionMethod_HasNoReceiverToSkip()
    {
        var method = Resolve("Fluent");

        Assert.NotNull(method.ReducedFrom);
        Assert.Equal(0, KnownSymbols.ReceiverOffset(method));
        Assert.Equal(0, KnownSymbols.ArgumentIndex(method.Parameters[0]));
    }

    /// <summary>
    /// The same method unreduced, as the static spelling resolves it and as an
    /// <c>IInvocationOperation.TargetMethod</c> always answers: parameter 0 is the receiver, which argument
    /// space has no index for.
    /// </summary>
    [Fact]
    public void AnUnreducedExtensionMethod_SkipsItsReceiver()
    {
        var method = Resolve("Static");

        Assert.Null(method.ReducedFrom);
        Assert.True(method.IsExtensionMethod);
        Assert.Equal(1, KnownSymbols.ReceiverOffset(method));
        Assert.Equal(-1, KnownSymbols.ArgumentIndex(method.Parameters[0]));
        Assert.Equal(0, KnownSymbols.ArgumentIndex(method.Parameters[1]));
    }

    /// <summary>
    /// The two spellings of one call agree about which parameter carries the first written argument, which
    /// is the whole point of asking one rule instead of six.
    /// </summary>
    [Fact]
    public void BothSpellings_AgreeOnTheFirstWrittenArgument()
    {
        var reduced = Resolve("Fluent");
        var unreduced = Resolve("Static");

        Assert.Equal(
            KnownSymbols.ArgumentIndex(reduced.Parameters[0]),
            KnownSymbols.ArgumentIndex(unreduced.Parameters[1]));
        Assert.Equal(
            reduced.Parameters.Length - KnownSymbols.ReceiverOffset(reduced),
            unreduced.Parameters.Length - KnownSymbols.ReceiverOffset(unreduced));
    }

    /// <summary>
    /// C# 14's extension-block spelling. <see cref="IMethodSymbol.IsExtensionMethod"/> is
    /// <see langword="false"/> for it, and Roslyn's parameter list excludes the receiver already, so 0 is
    /// the right answer and not merely a lucky one.
    /// </summary>
    [Fact]
    public void AnExtensionBlockMember_HasNoReceiverInItsParameterList()
    {
        var method = Resolve("Block");

        Assert.False(method.IsExtensionMethod);
        Assert.Equal(0, KnownSymbols.ReceiverOffset(method));
        Assert.Equal(0, KnownSymbols.ArgumentIndex(method.Parameters[0]));
        Assert.Equal("times", method.Parameters[0].Name);
    }

    [Fact]
    public void AnOrdinaryInstanceMethod_HasNoReceiverToSkip()
    {
        var method = Resolve("Method");

        Assert.Equal(0, KnownSymbols.ReceiverOffset(method));
        Assert.Equal(0, KnownSymbols.ArgumentIndex(method.Parameters[0]));
    }

    /// <summary>
    /// An indexer's parameter has no receiver ahead of it, and its containing symbol is a property rather
    /// than a method, which is the arm <see cref="KnownSymbols.ArgumentIndex"/> answers by ordinal.
    /// </summary>
    [Fact]
    public void AnIndexerParameter_IsAtItsOwnOrdinal()
    {
        var symbols = KnownSymbols.TryCreate(Compilation);
        Assert.NotNull(symbols);
        var indexer = symbols!.ElementIndexer;
        Assert.NotNull(indexer);

        Assert.Equal(0, KnownSymbols.ArgumentIndex(indexer!.Parameters[0]));
    }
}

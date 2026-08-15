using System.Linq;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds every decoration that can be written with no argument to the one thing its own declaration cannot
/// state: that <c>ClassifyDecoration</c> classifies it ahead of the argument guard.
/// </summary>
/// <remarks>
/// <para>
/// A decoration that reaches that guard with nothing bound returns null without reporting, and the whole
/// <c>Body</c> becomes BCF1003 — a diagnostic that names the design-time expression rather than the
/// decoration, so the author is told the body is untranslatable and not which call did it. Nothing else
/// fails: the surface still compiles, the decoration still binds, and the output simply lacks it. That is
/// the trap #372 was opened about, and a comment beside the guard could not close it.
/// </para>
/// <para>
/// Discovered reflectively rather than listed, so a third zero-argument decoration is covered by declaring
/// it. The routing rule is read from <see cref="RenderExpressionAnalyzer.RoutesBeforeArgumentGuard"/>
/// rather than transcribed, on the same terms as <c>FrameDecorationChannelCoverageTests</c> and
/// <c>KnownSymbolsSyncTests</c>: the test reads the real rule instead of a copy of it, so it cannot be
/// satisfied by writing the copy twice.
/// </para>
/// </remarks>
public sealed class ZeroArgumentDecorationRoutingTests
{
    [Fact]
    public void EveryZeroArgumentDecoration_RoutesBeforeTheArgumentGuard()
    {
        var compilation = CompilationTestHost.CreateCompilation("");
        var symbols = KnownSymbols.TryCreate(compilation);
        Assert.NotNull(symbols);

        var decorations = compilation.GetTypeByMetadataName("BlazorCodeFirst.Decorations");
        Assert.NotNull(decorations);

        // Parameter count less the receiver, not the count itself: these symbols are unreduced, so every
        // one of them declares its ElementView receiver as a parameter.
        var zeroArgument = decorations!.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method is { IsExtensionMethod: true, DeclaredAccessibility: Accessibility.Public }
                && method.Parameters.Length - KnownSymbols.ReceiverOffset(method) == 0)
            .ToList();

        // A resolution failure would otherwise make the guard vacuous rather than red.
        Assert.NotEmpty(zeroArgument);

        foreach (var method in zeroArgument)
        {
            Assert.True(
                RenderExpressionAnalyzer.RoutesBeforeArgumentGuard(symbols!.ClassifySurfaceMethod(method)),
                $"'.{method.Name}()' can be written with no argument, but its classification is not one " +
                $"ClassifyDecoration routes ahead of the argument guard. Written below that guard it " +
                $"disappears into a BCF1003 on the whole Body. Give it an arm above the guard and add its " +
                $"kind to RenderExpressionAnalyzer.RoutesBeforeArgumentGuard.");
        }
    }
}

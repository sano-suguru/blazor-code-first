using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Why the children channel takes <see cref="string"/> and <c>View</c> and has no numeric spelling
/// (<c>DESIGN.md</c> §4.1, #245). The decision turns on there being nothing mechanical to separate
/// <c>Div[_n]</c> from <c>Div[$"{_n}"]</c>, so what is pinned here is the sameness: the same call, the
/// same frame, and the same standing outside the static fold. <c>NonStringValueFormattingTests</c> in
/// <c>BlazorCodeFirst.WebAppTests</c> holds the runtime half — that both spellings format at the call.
/// </summary>
public sealed class ChildValueSpellingTests
{
    private static string GenerateBody(string bodyExpression)
    {
        var source = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private const int Three = 3;
                private const string Word = "three";
                private int _n = 3;

                protected override View Body => {{bodyExpression}};
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        CompilationTestHost.AssertOutputCompiles(result);
        return Assert.Single(result.GeneratedSources).SourceText.ToString();
    }

    /// <summary>
    /// The premise the whole decision rests on: a number is not a child today. C# rejects it before the
    /// generator sees the expression, so nothing in this repository reports it and nothing else pins it —
    /// a widened indexer or an added conversion would make <c>DESIGN.md</c> §4.1 false in silence.
    /// </summary>
    [Fact]
    public void ANumericChild_IsRejectedByCSharp()
    {
        var source = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class Counter : BodyComponentBase
            {
                private int _n = 3;

                protected override View Body => Div[_n];
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(
            result.OutputCompilation.GetDiagnostics(),
            diagnostic => diagnostic.Id == "CS1503");
    }

    /// <summary>
    /// The interpolation is transplanted into <c>RenderView</c> verbatim and evaluated as the
    /// <c>AddContent</c> argument. That is what makes the formatting happen on the render thread: there is
    /// no earlier point at which the string could exist.
    /// </summary>
    [Fact]
    public void InterpolatedChild_IsEvaluatedInsideRenderView()
    {
        var generated = GenerateBody("""Span[$"Count: {_n}"]""");

        Assert.Contains("""__builder.AddContent(1, $"Count: {_n}");""", generated, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every numeric type a child spelling would plausibly accept resolves to one overload,
    /// <c>AddContent(int, object?)</c>, while a string and an interpolated string resolve to
    /// <c>AddContent(int, string?)</c>. Asked of Roslyn against the real <c>RenderTreeBuilder</c> metadata
    /// rather than asserted from the docs, because the answer is what a numeric child would reach and no
    /// such child can be written today to observe it.
    /// </summary>
    [Fact]
    public void ANumericChild_WouldReachTheObjectOverload()
    {
        const string probe = """
            using System;
            using Microsoft.AspNetCore.Components.Rendering;

            public class Probe
            {
                public void M(RenderTreeBuilder b, int i, double d, decimal m, DateTime t, DayOfWeek e,
                    string? s)
                {
                    b.AddContent(0, i);
                    b.AddContent(1, d);
                    b.AddContent(2, m);
                    b.AddContent(3, t);
                    b.AddContent(4, e);
                    b.AddContent(5, s);
                    b.AddContent(6, $"{i}");
                }
            }
            """;

        var compilation = CompilationTestHost.CreateCompilation(probe);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);

        // Null rather than null-forgiving: an unresolved call then shows as null at a named index, instead
        // of throwing an unattributed NullReferenceException from inside the projection.
        var chosen = tree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(invocation => model.GetSymbolInfo(invocation).Symbol?.ToDisplayString())
            .ToList();

        const string ObjectOverload =
            "Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder.AddContent(int, object?)";
        const string StringOverload =
            "Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder.AddContent(int, string?)";

        Assert.Equal(
            [
                ObjectOverload, ObjectOverload, ObjectOverload, ObjectOverload, ObjectOverload,
                StringOverload, StringOverload,
            ],
            chosen);
    }

    /// <summary>
    /// Interpolating a number keeps the child out of the static fold, because C# stops treating the string
    /// as a constant. So the frame-count asymmetry a numeric spelling is sometimes said to introduce is
    /// already here: two spellings of the same static content, one folded and one not.
    /// </summary>
    [Theory]
    [InlineData("""Span[$"n={3}"]""")]
    [InlineData("""Span[$"n={Three}"]""")]
    public void ANumberInAnInterpolation_DoesNotFold(string bodyExpression)
    {
        var generated = GenerateBody(bodyExpression);

        Assert.Contains("__builder.AddContent(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("__builder.AddMarkupContent(", generated, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other side of that split, so the assertion above cannot pass by the fold having stopped
    /// working: a constant string folds, whether written plainly or interpolated.
    /// </summary>
    [Theory]
    [InlineData("""Span["n=3"]""")]
    [InlineData("""Span[$"n={Word}"]""")]
    public void AConstantStringChild_Folds(string bodyExpression)
    {
        var generated = GenerateBody(bodyExpression);

        Assert.Contains("__builder.AddMarkupContent(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("__builder.AddContent(", generated, StringComparison.Ordinal);
    }
}

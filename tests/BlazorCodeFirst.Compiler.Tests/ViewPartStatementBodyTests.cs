using System.Linq;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// A <c>[ViewPart]</c> body on the Transplantable path (ARCHITECTURE.md §2.3): the third position that
/// accepts the block shape <c>Body</c>, <c>Chrome</c>, and <c>ForEach</c> content already accept. It was
/// BCF1002 until the block's own locals took names expansion mints (#336), because the statements are
/// copied into every call site.
/// </summary>
public sealed class ViewPartStatementBodyTests
{
    /// <summary>A component calling <c>Part</c> twice, whose part body is <c>$BODY$</c>.</summary>
    private const string Host = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Div[Part("a"), Part("b")];

            [ViewPart]
            private static View Part(string title)
            $BODY$
        }
        """;

    private static GeneratorRunResult Run(string body) =>
        CompilationTestHost.RunGenerator(Host.Replace("$BODY$", body));

    [Fact]
    public void ViewPart_WhenBodyIsABlockWithOneTrailingReturn_ExpandsAtEveryCallSite()
    {
        var result = Run("""
            {
                    var label = title.ToUpperInvariant();
                    return Span[label];
                }
            """);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);

        // One declaration per call site, each named from its own block's preorder ordinal. The two land
        // in one scope, which is what kept this shape out until #336.
        Assert.Equal(
            2,
            System.Text.RegularExpressions.Regex.Count(generated, @"string __bcf_local_\d+_0 ="));
    }

    [Fact]
    public void ViewPart_WhenBodyIsABlockAndTheDeclarationReadsAParameter_BindsThroughTheArgumentLocal()
    {
        // The statements take the same substitution the returned expression does, so a parameter read in
        // one is the argument local the call site declared, not the parameter's written name.
        var generated = Assert.Single(
            Run("""
                {
                        var label = title;
                        return Span[label];
                    }
                """).GeneratedSources).SourceText.ToString();

        Assert.Contains("string __bcf_arg_", generated);
        Assert.DoesNotContain("= title;", generated);
    }

    [Fact]
    public void ViewPart_WhenBodyIsABlockAndTheReturnTypeIsSlotView_StillCountsTheSlot()
    {
        // The slot count reads the whole body, so a part that names Slot from the returned expression is
        // accepted and one that never names it is still BCF3025.
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Card()["x"];

                [ViewPart]
                private static SlotView Card()
                {
                    var wrapper = "card";
                    return Section.Class(wrapper)[Slot];
                }
            }
            """);

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Theory]
    // Two returns: each needs a sequence space of its own, which is the wider Transplantable slice.
    [InlineData(
        "two returns",
        """
        {
                if (title.Length == 0)
                    return Span["empty"];

                return Span[title];
            }
        """)]
    // Native control flow, for the same reason.
    [InlineData(
        "native foreach",
        """
        {
                foreach (var c in title)
                    System.Console.WriteLine(c);

                return Span[title];
            }
        """)]
    // The builder the generated frames are written against, which a part's statements share the scope of
    // once they are copied to the call site.
    [InlineData(
        "the builder's name",
        """
        {
                var __builder = title;
                return Span[__builder];
            }
        """)]
    // A local spelled with the generator's reserved prefix. Held here and not only at the other two
    // positions because the hole splice cites this refusal: it is why a declaration reaching both the
    // rename arm and the render-variable arm is a designation and never a declarator
    // (ExpressionTemplateFactory.AuthoredContextNameHygiene).
    [InlineData(
        "generator-reserved local name",
        """
        {
                var __bcf_label = title;
                return Span[__bcf_label];
            }
        """)]
    public void ViewPart_WhenBodyIsOutsideTheAcceptedShape_StaysBCF1002(string shape, string body)
    {
        var result = Run(body);

        Assert.True(
            result.Diagnostics.Any(d => d.Id == "BCF1002"),
            $"{shape}: expected BCF1002, got [{string.Join(", ", result.Diagnostics.Select(d => d.Id))}].");
    }
}

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// A native `if`/`else` as the last statement of a design-time expression getter (ARCHITECTURE.md
/// §2.3/§5.3): the block is region-wrapped, and each arm's content is drawn through a synthesized
/// `RenderFragment` rather than statically assigned.
/// </summary>
public sealed class BodyTransplantIfTests
{
    /// <summary>A component with a field <c>_flag</c>/<c>_a</c>/<c>_b</c> and a <c>Body</c> getter of <c>$GETTER$</c>.</summary>
    private const string Host = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        public partial class C : BodyComponentBase
        {
            private bool _flag = true;
            private bool _a;
            private bool _b;

            protected override View Body
            $GETTER$
        }
        """;

    private static GeneratorRunResult Run(string getter) =>
        CompilationTestHost.RunGenerator(Host.Replace("$GETTER$", getter));

    private const string IfElseGetter = """
        {
                get
                {
                    if (_flag)
                    {
                        return Span["yes"];
                    }
                    else
                    {
                        return Span["no"];
                    }
                }
            }
        """;

    [Fact]
    public void Body_WhenGetterEndsInNativeIfElse_TransplantsIntoARegion()
    {
        var result = Run(IfElseGetter);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");
        Assert.Single(result.Diagnostics, d => d.Id == "BCF2002");

        Assert.Contains("__builder.OpenRegion(0);", generated);
        Assert.Contains("if (_flag)", generated);
        Assert.Contains("else", generated);
        Assert.Contains("__builder.CloseRegion();", generated);

        // Each arm's content is a freshly synthesized RenderFragment lambda, not FragmentOf: an SSC
        // expression's View is always empty (ARCHITECTURE.md's BCF2001/BCF3030 premise), so wrapping
        // Span["yes"] in FragmentOf would silently render nothing.
        Assert.DoesNotContain("FragmentOf", generated);
        Assert.Contains("__builder) =>", generated);

        // Span["yes"]/Span["no"] fold to one AddMarkupContent frame inside each synthesized fragment
        // (StaticMarkupSerializer's static-subtree folding, ARCHITECTURE.md §2.7(D)) -- the fragment's
        // own independent 0-based sequence, unrelated to the outer region's numbering.
        Assert.Contains("__builder.AddMarkupContent(0, \"<span>yes</span>\");", generated);
        Assert.Contains("__builder.AddMarkupContent(0, \"<span>no</span>\");", generated);

        // Both arms' outer AddContent share the region's one reserved sequence number.
        Assert.Contains("__builder.AddContent(1, ", generated);

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenIfHasNoElseAndNoTrailingStatement_ReportsBcf1004()
    {
        // `Body` must produce a View on every path. An `if` with no `else`, as the block's only/last
        // statement, does not cover the path where the condition is false -- this is CS0161 ("not all
        // code paths return a value") on the ORIGINAL author source before BCF analysis even runs, so
        // the getter never reaches TryReadTransplantableIf's `if`-is-the-last-statement branch at all
        // for csc's own reasons. What DOES reach TryReadTransplantableIf and gets rejected there is a
        // native `if` that is NOT the block's last statement (an explicit `return` after it): the `if`
        // is then a LEADING statement, and TryReadLeadingStatements only accepts a local declaration or
        // an expression statement as leading -- an `IfStatementSyntax` fails that test, so this shape is
        // rejected by both TryReadTransplantableBlock and TryReadTransplantableIf and falls through to
        // BCF1004, the same diagnostic as before this feature existed.
        const string Getter = """
            {
                    get
                    {
                        if (_flag)
                        {
                            return Span["yes"];
                        }
                        return Span["fallback"];
                    }
                }
            """;
        var result = Run(Getter);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1004");
    }

    [Fact]
    public void Body_WhenElseIfChainThreeDeep_SharesOneRegionAndOneSequenceAcrossAllArms()
    {
        const string Getter = """
            {
                    get
                    {
                        if (_a)
                        {
                            return Span["a"];
                        }
                        else if (_b)
                        {
                            return Span["b"];
                        }
                        else
                        {
                            return Span["c"];
                        }
                    }
                }
            """;
        var result = Run(Getter);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF1004");

        // Reported once for the whole chain, at the outermost `if` -- not once per `else if` link.
        Assert.Single(result.Diagnostics, d => d.Id == "BCF2002");

        // Exactly one region for the whole chain -- an `else if` must not open a second, nested one.
        Assert.Single(
            System.Text.RegularExpressions.Regex.Matches(generated, "__builder\\.OpenRegion\\("));

        // All three arms share the same sequence number: only one ever runs per render.
        Assert.Equal(
            3,
            System.Text.RegularExpressions.Regex.Count(generated, "__builder\\.AddContent\\(1,"));

        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Body_WhenArmDeclaresReservedName_ReportsBcf1004()
    {
        const string Getter = """
            {
                    get
                    {
                        if (_flag)
                        {
                            var __builder = 1;
                            return Span[__builder.ToString()];
                        }
                        else
                        {
                            return Span["no"];
                        }
                    }
                }
            """;
        var result = Run(Getter);
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1004");
    }
}

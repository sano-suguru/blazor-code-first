namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The shapes <c>If</c>'s <c>then</c>/<c>otherwise</c> branches accept: the same five <c>ForEach</c>'s
/// content does (#317), at the zero-parameter arity <c>Func&lt;View&gt;</c> requires -- an inline
/// expression lambda, a block reaching one trailing <c>return</c>, a block ending in a native
/// <c>if</c>/<c>else</c> or <c>switch</c> (each degrading through BCF2002), and a zero-parameter method
/// group, read as the call it stands for and answered by the same three-way branch any other call gets.
/// </summary>
public sealed class IfBranchShapeTests
{
    /// <summary>A component whose <c>If</c> <c>then</c> branch is <c>$BRANCH$</c>.</summary>
    private const string Host = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.If(true, $BRANCH$);
        }
        """;

    private const string ExpressionBranch = "() => Html.Span[\"hi\"]";

    private const string BlockBranch = """
        () =>
                {
                    var label = "hi";
                    return Html.Span[label];
                }
        """;

    private const string BlockIfBranch = """
        () =>
                {
                    if (true)
                    {
                        return Html.Span["a"];
                    }
                    else
                    {
                        return Html.Span["-"];
                    }
                }
        """;

    private const string BlockSwitchBranch = """
        () =>
                {
                    switch (1)
                    {
                        case 1:
                            return Html.Span["a"];
                        default:
                            return Html.Span["-"];
                    }
                }
        """;

    private static GeneratorRunResult Run(string branch) =>
        CompilationTestHost.RunGenerator(Host.Replace("$BRANCH$", branch));

    [Fact]
    public void IfBranch_WhenExpressionLambda_IsAccepted()
    {
        var result = Run(ExpressionBranch);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3044");
        Assert.Contains(result.GeneratedSources, s => s.HintName.Contains('C'));
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IfBranch_WhenBlockBodiedWithOneTrailingReturn_TransplantsTheStatements()
    {
        var result = Run(BlockBranch);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3044");
        Assert.Contains("""string label = "hi";""", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IfBranch_WhenBlockEndsInNativeIfElse_TransplantsIntoARegion()
    {
        var result = Run(BlockIfBranch);
        Assert.Single(result.GeneratedSources);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF3044");
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF2002");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IfBranch_WhenBlockEndsInNativeSwitch_TransplantsIntoARegion()
    {
        var result = Run(BlockSwitchBranch);
        Assert.Single(result.GeneratedSources);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF1003" or "BCF3044");
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF2002");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IfBranch_WhenMethodGroupIsAViewPart_ExpandsStatically()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Html.If(true, Render);

                [ViewPart]
                private static View Render() => Html.Span["hi"];
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF3044" or "BCF3030");
        // A constant-child Span folds to static markup, so the proof of static expansion is that the
        // then branch's content -- Render's own body, not a runtime fragment call -- lands inside the
        // region at all: OpenRegion/CloseRegion wrap a real AddMarkupContent rather than a synthesized
        // RenderFragment invocation.
        Assert.Contains("AddMarkupContent(1, \"<span>hi</span>\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void IfBranch_WhenMethodGroupBuildsFromTheSurfaceWithoutTheAttribute_ReportsBCF3030()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Html.If(true, Render);

                private static View Render() => Html.Span["hi"];
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3044");
    }

    [Fact]
    public void IfBranch_WhenTheDelegateIsNotABareMethodGroup_ReportsBCF3044()
    {
        // A constructed delegate names no callee at the call site, so none of the three answers a bare
        // method group gets applies and the shape restriction stands (mirrors ForEach's own content).
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using System;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Html.If(true, new Func<View>(Render));

                private static View Render() => Html.Span["hi"];
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3044");
    }

    [Fact]
    public void IfBranch_WhenBlockHasMoreThanOneReturn_ReportsBCF3044()
    {
        // A second return needs a sequence space of its own, which is the wider Transplantable slice and
        // not this one -- the same reason ForEach's content excludes it (mirrors ForEachTransplantTests'
        // reserved-name theory, which pairs "two returns" with the same rejection).
        var result = Run("""
            () =>
                    {
                        return Html.Span["a"];
                        return Html.Span["b"];
                    }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3044");
    }

    [Fact]
    public void IfBranch_WhenItDeclaresAReservedName_ReportsBCF3044()
    {
        var result = Run("""
            () =>
                    {
                        var __builder = "hi";
                        return Html.Span[__builder];
                    }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3044");
    }

    [Fact]
    public void IfBranch_WhenAnonymousMethod_ReportsBCF3044()
    {
        var result = Run("""delegate() { return Html.Span["hi"]; }""");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3044");
    }

    /// <summary>
    /// A rejected branch inside a <c>[ViewPart]</c> body: the shape itself (a lowered <c>If(...)</c> call
    /// as the body's own returned expression) is one <c>TryReadBody</c> already accepts, so the failure is
    /// BCF3044 alone -- not wrapped in a second BCF1002, which is reserved for a body outside all the
    /// shapes <c>TryReadBody</c> recognizes at all.
    /// </summary>
    [Fact]
    public void IfBranch_WhenRejectedInsideAViewPartBody_ReportsBCF3044NotBCF1002()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Widget();

                [ViewPart]
                private static View Widget() =>
                    Html.If(true, delegate() { return Html.Span["hi"]; });
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3044");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1002");
    }
}

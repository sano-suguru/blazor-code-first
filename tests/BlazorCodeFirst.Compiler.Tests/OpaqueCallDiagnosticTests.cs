namespace BlazorCodeFirst.Compiler.Tests;

public sealed class OpaqueCallDiagnosticTests
{
    /// <summary>
    /// A component calling a helper whose declaration carries <c>$ATTRIBUTE$</c>. The helper builds its
    /// <c>View</c> from the design-time surface, so without the attribute the call renders nothing.
    /// </summary>
    private const string SurfaceBuiltHelper = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div[Card("hello")];

            $ATTRIBUTE$
            private static View Card(string title) => Html.Span[title];
        }
        """;

    /// <summary>A helper whose <c>View</c> comes from a real <c>RenderFragment</c>: the genuine Opaque case.</summary>
    private const string FragmentBackedHelper = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;

        public partial class C : BodyComponentBase
        {
            protected override View Body => Html.Div[Wrap()];

            private static View Wrap()
            {
                RenderFragment fragment = builder => builder.AddContent(0, "x");
                return fragment;
            }
        }
        """;

    /// <summary>
    /// <c>Saved</c> is a non-indexer property, not an <c>ElementTags</c> entry, and not <c>Html.Slot</c>,
    /// so the property arm must fall through to <see langword="null"/> rather than read it as a slot: the
    /// slot arm's own check (<c>ClassifySlot</c>) never compares against <c>resolvedProperty</c> at all, it
    /// only asks whether <c>Html.Slot</c>'s own ordinal is registered, so misrouting <em>any</em> unrelated
    /// property here reports BCF3025 in its name instead of translating -- or failing to translate -- the
    /// property the author actually wrote (#487).
    /// </summary>
    [Fact]
    public void BarePropertyOfTypeView_WhenNeitherAnElementNorSlot_IsNotReadAsASlot()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private static View Saved => Html.Div;

                protected override View Body => Saved;
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3025");
    }

    /// <summary>
    /// <c>otherwise</c> bound to a method group rather than an inline lambda: <c>ExtractLambdaBody</c>
    /// cannot recover a body from it, and the check right after must still short-circuit rather than pass
    /// that <see langword="null"/> body on to <c>DeclaresReservedName</c>, which throws on it (#487).
    /// </summary>
    [Fact]
    public void IfOtherwiseBranch_WhenNotAnInlineLambda_ReportsBCF1003WithoutThrowing()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                private static View NotALambda() => Div;

                protected override View Body =>
                    If(true, then: () => Span["ok"], otherwise: NotALambda);
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    [Fact]
    public void ViewReturningCall_WhenCalleeBuildsFromTheDesignTimeSurface_ReportsBCF3030AndNotBCF1003()
    {
        var result = CompilationTestHost.RunGenerator(
            SurfaceBuiltHelper.Replace("$ATTRIBUTE$", string.Empty));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");

        // BCF3030 names the fix. BCF1003 would only say the body could not be translated, and
        // ComponentModelFactory.Expand suppresses it once an actionable error is present.
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");
    }

    /// <summary>
    /// The same shape as <see cref="ViewReturningCall_WhenCalleeBuildsFromTheDesignTimeSurface_ReportsBCF3030AndNotBCF1003"/>,
    /// but the callee's declaration lives in a different file (a different <c>SyntaxTree</c>) from the call
    /// site. <c>ComputeBodyBuildsFromDesignTimeSurface</c> picks between reusing <c>context.SemanticModel</c>
    /// and building a fresh one for the callee's own tree; a semantic query run through the wrong model for
    /// a node's tree throws, so this pins that the cross-file branch is taken and answers correctly rather
    /// than merely not crashing.
    /// </summary>
    [Fact]
    public void ViewReturningCall_WhenCalleeIsDeclaredInAnotherFile_ReportsBCF3030()
    {
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;

                public partial class C : BodyComponentBase
                {
                    protected override View Body => Html.Div[Card("hello")];
                }
                """),
            ("Card.cs", """
                using BlazorCodeFirst;

                public partial class C
                {
                    private static View Card(string title) => Html.Span[title];
                }
                """));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF1003");
    }

    /// <summary>
    /// A callee whose only reference to the design-time surface is a bare property access with no
    /// invocation or indexer over it (<c>Html.Div</c>, converted to <c>View</c> at the <c>return</c>).
    /// <c>ComputeBodyBuildsFromDesignTimeSurface</c>'s syntax-kind prefilter has to admit a plain
    /// <c>MemberAccessExpressionSyntax</c>, not only an invocation or an indexer, or this callee is missed
    /// entirely and reported as the wrong kind (BCF2001, opaque) instead of BCF3030.
    /// </summary>
    [Fact]
    public void ViewReturningCall_WhenCalleeIsABarePropertyAccess_ReportsBCF3030()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Html.Div[Bare()];

                private static View Bare() => Html.Span;
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF2001");
    }

    /// <summary>
    /// The same bare-property-access shape, but reached through <c>using static BlazorCodeFirst.Html</c>
    /// (the exact spelling <c>tests/diagnostic-fixtures/GeneratorDelivery.ProjectReference/Bcf2001Bcf3030.cs</c>
    /// uses). Written this way, the callee's design-time reference is a lone
    /// <c>IdentifierNameSyntax</c> ("Span") with no enclosing <c>MemberAccessExpressionSyntax</c> at all —
    /// unlike the qualified <c>Html.Span</c> spelling above, where the parent-name skip in
    /// <c>ComputeBodyBuildsFromDesignTimeSurface</c> is redundant with the outer member-access node right
    /// beside it. Only this unqualified spelling makes that skip's own correctness observable: inverting
    /// it (or flipping its <c>==</c>) makes the walk skip the one node carrying the answer, with nothing
    /// else to catch it, and BCF3030 is missed.
    /// </summary>
    [Fact]
    public void ViewReturningCall_WhenCalleeIsABarePropertyAccess_ViaUsingStatic_ReportsBCF3030()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            public partial class C : BodyComponentBase
            {
                protected override View Body => Div[Bare()];

                private static View Bare() => Span;
            }
            """);

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF2001");
    }

    [Fact]
    public void ViewReturningCall_WhenCalleeIsAViewPart_ReportsNothing()
    {
        var result = CompilationTestHost.RunGenerator(
            SurfaceBuiltHelper.Replace("$ATTRIBUTE$", "[ViewPart]"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3030");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF2001");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ViewReturningCall_WhenCalleeDoesNotUseTheSurface_ReportsBCF2001AndRendersTheFragment()
    {
        var result = CompilationTestHost.RunGenerator(FragmentBackedHelper);

        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BCF2001");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Info, diagnostic.Severity);

        // The call itself is fully qualified by ExpressionTemplateFactory, because the generated file
        // carries no using directives.
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();
        Assert.Contains(
            "global::BlazorCodeFirst.CompilerServices.ViewRuntime.FragmentOf(global::C.Wrap())",
            generated);

        // Blazor opens a region for the fragment itself, exactly as it does for RenderFragmentContentNode,
        // so the emission writes no OpenRegion of its own.
        Assert.DoesNotContain("OpenRegion", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ForEachContent_WhenRootIsAnOpaqueCall_ReportsBCF3003()
    {
        const string Source = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using System.Collections.Generic;

            public partial class C : BodyComponentBase
            {
                private readonly List<string> _items = new() { "a" };

                protected override View Body =>
                    Html.ForEach(_items, x => x, x => Wrap(x));

                private static View Wrap(string text)
                {
                    RenderFragment fragment = builder => builder.AddContent(0, text);
                    return fragment;
                }
            }
            """;

        var result = CompilationTestHost.RunGenerator(Source);

        // A fragment opens no keyable frame, so SetKey has nothing to attach to. Same rule that already
        // rejects Fragment and Raw as content roots.
        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }
}

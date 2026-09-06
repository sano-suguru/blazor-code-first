using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Analysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The correctness gate for issue #480's registry-subset alternative. For every component in each
/// scenario below, <c>ComponentModelFactory.Expand</c> against <see cref="ViewPartReachability"/>'s
/// subset must equal <c>Expand</c> against the full registry, on both the emitted model and its
/// diagnostics. <see cref="RegistrySubsetCostTests"/> measures whether the subset is cheaper; this
/// class is the prerequisite that measurement depends on — <see cref="ViewPartReachability"/>'s own
/// remarks document the closure as an over-approximation by design, and this is what verifies that
/// promise empirically rather than by argument alone. No figure is published unless every scenario
/// here passes.
/// </summary>
public sealed class ViewPartReachabilityEquivalenceTests
{
    [Fact]
    public void KeyedForEachCall_IsEquivalent()
    {
        AssertSubsetEquivalence(CompilationTestHost.RunGenerator(
            ("Support.cs", """
                namespace TestNs;

                public sealed record Item(int Id, string Name);
                """),
            ("Widgets.cs", """
                using System.Collections.Generic;
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace TestNs;

                public static class Widgets
                {
                    [ViewPart]
                    public static View Widget(List<Item> items) =>
                        ForEach(items, key: i => i.Id, content: i => Span[i.Name]);
                }
                """),
            ("Caller.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace TestNs;

                public partial class Caller : BodyComponentBase
                {
                    protected override View Body => Widgets.Widget(new());
                }
                """),
            ("NonCaller.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace TestNs;

                public partial class NonCaller : BodyComponentBase
                {
                    protected override View Body => Span["Unrelated"];
                }
                """)));
    }

    /// <summary>Transitive: a component's call reaches a second view part only through the first one's body.</summary>
    [Fact]
    public void TransitiveViewPartCall_IsEquivalent()
    {
        AssertSubsetEquivalence(CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace TestNs;

                public static class Widgets
                {
                    [ViewPart]
                    public static View Inner(string value) => Span[value];

                    [ViewPart]
                    public static View Outer(string value) => Fragment[Inner(value)];
                }
                """),
            ("Caller.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace TestNs;

                public partial class Caller : BodyComponentBase
                {
                    protected override View Body => Widgets.Outer("x");
                }
                """)));
    }

    /// <summary>
    /// A <c>ViewPartCallNode</c>'s content arguments carry <c>RenderNode</c> subtrees of their own, so a
    /// call passed as another call's argument is layer 1, not layer 2, and must be walked the same way.
    /// </summary>
    [Fact]
    public void ViewPartCallInsideContentArgument_IsEquivalent()
    {
        AssertSubsetEquivalence(CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace TestNs;

                public static class Widgets
                {
                    [ViewPart]
                    public static View Badge(string value) => Span[value];

                    [ViewPart]
                    public static View Frame(View content) => Div[content];
                }
                """),
            ("Panel.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace TestNs;

                public partial class Panel : BodyComponentBase
                {
                    protected override View Body =>
                        Widgets.Frame(Widgets.Badge("inside a content argument"));
                }
                """)));
    }

    /// <summary>A mutual cycle must terminate the walk rather than recurse forever.</summary>
    [Fact]
    public void MutualCycle_IsEquivalent()
    {
        AssertSubsetEquivalence(CompilationTestHost.RunGenerator(
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    [ViewPart]
                    private static View Ping() => Pong();

                    [ViewPart]
                    private static View Pong() => Ping();

                    protected override View Body => Ping();
                }
                """)));
    }

    /// <summary>An unresolved call (no declaration reachable) must resolve identically against both registries.</summary>
    [Fact]
    public void UnresolvedCall_IsEquivalent()
    {
        AssertSubsetEquivalence(CompilationTestHost.RunGenerator(
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    // Invalid declaration (not static): TryGet succeeds but Definition is null.
                    [ViewPart]
                    private View Helper() => Span["x"];

                    protected override View Body => Helper();
                }
                """)));
    }

    /// <summary>A cross-type access-requirement failure inside the callee's own body.</summary>
    [Fact]
    public void AccessRequirementFailure_IsEquivalent()
    {
        AssertSubsetEquivalence(CompilationTestHost.RunGenerator(
            ("Widgets.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public static class Widgets
                {
                    private static string Secret() => "s";

                    [ViewPart]
                    public static View Label(string value) => Span[Secret() + value];
                }
                """),
            ("Counter.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                public partial class Counter : BodyComponentBase
                {
                    protected override View Body => Widgets.Label("x");
                }
                """)));
    }

    /// <summary>
    /// The CSS-scope half: the callee's own <c>.cs.css</c> scope must be reachable through
    /// <c>entry.FilePath</c>, not just the caller's own file.
    /// </summary>
    [Fact]
    public void CalleeCssScope_IsEquivalent()
    {
        AssertSubsetEquivalence(CompilationTestHost.RunGeneratorWithCssScopes(
            sources:
            [
                ("Widgets.cs", """
                    using BlazorCodeFirst;
                    using static BlazorCodeFirst.Html;

                    namespace TestNs;

                    public static class Widgets
                    {
                        [ViewPart]
                        public static View Widget(string value) => Span[value];
                    }
                    """),
                ("Caller.cs", """
                    using BlazorCodeFirst;
                    using static BlazorCodeFirst.Html;

                    namespace TestNs;

                    public partial class Caller : BodyComponentBase
                    {
                        protected override View Body => Widgets.Widget("x");
                    }
                    """),
            ],
            cssScopes:
            [
                ("Widgets.cs.css", "bcf-widgets"),
                ("Caller.cs.css", "bcf-caller"),
            ]));
    }

    /// <summary>
    /// <see cref="ViewPartReachability"/> trusts <see cref="KeyabilityResolver.Children"/> to be the one
    /// exhaustive child walk over <see cref="RenderNode"/>. If a future subtype is added and
    /// <c>Children</c> is not updated for it, <c>Children</c> itself throws
    /// <see cref="NotSupportedException"/> at runtime rather than silently under-approximating, but that
    /// only fires when a test happens to exercise the new node. This tripwire fires unconditionally: a
    /// changed count means a subtype was added or removed, and is a prompt to go add its case there.
    /// </summary>
    [Fact]
    public void RenderNodeSubtypeCount_MatchesKeyabilityResolverChildrenCoverage()
    {
        var subtypeCount = typeof(RenderNode).Assembly.GetTypes()
            .Count(static t => t.IsSealed && typeof(RenderNode).IsAssignableFrom(t));

        Assert.True(
            subtypeCount == 15,
            $"Expected 15 concrete RenderNode subtypes (the count KeyabilityResolver.Children and "
                + $"ViewPartReachability's exhaustiveness argument were written against) but found "
                + $"{subtypeCount}. If a subtype was added or removed, update both Children and this "
                + "count together.");
    }

    private static void AssertSubsetEquivalence(GeneratorRunResult result)
    {
        var fullRegistry = (ViewPartRegistry)result.TrackedSteps["ViewPartRegistry"]
            .SelectMany(static s => s.Outputs).Single().Value!;
        var fullCss = (CssScopeRegistry)result.TrackedSteps["CssScopeRegistry"]
            .SelectMany(static s => s.Outputs).Single().Value!;

        var analyses = result.TrackedSteps["ComponentAnalysis"]
            .SelectMany(static s => s.Outputs)
            .Select(static o => (ComponentAnalysis)o.Value!)
            .ToImmutableArray();

        Assert.NotEmpty(analyses);

        foreach (var analysis in analyses)
        {
            var (subsetViewParts, subsetCssEntries) =
                ViewPartReachability.Collect(analysis, fullRegistry, fullCss);
            var subsetRegistry = ViewPartRegistry.Create(subsetViewParts.AsImmutableArray());
            var subsetCss = CssScopeRegistry.Create(subsetCssEntries.AsImmutableArray());

            var fullResult = ComponentModelFactory.Expand(analysis, fullRegistry, fullCss);
            var subsetResult = ComponentModelFactory.Expand(analysis, subsetRegistry, subsetCss);

            Assert.Equal(fullResult, subsetResult);
        }
    }
}

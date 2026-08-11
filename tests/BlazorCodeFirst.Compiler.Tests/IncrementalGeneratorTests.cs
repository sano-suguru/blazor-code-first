using System.Collections.Immutable;
using System.Linq;
using BlazorCodeFirst.Compiler.Analysis;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Validates incremental generator caching behavior: when only one of two components
/// changes across driver re-runs, the unchanged component model must be Cached/Unchanged
/// while the changed component must be recomputed.
/// </summary>
public sealed class IncrementalGeneratorTests
{
    private const string ComponentASource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public partial class ComponentA : BodyComponentBase
        {
            protected override View Body => Span["Hello A"];
        }
        """;

    private const string ComponentBSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public partial class ComponentB : BodyComponentBase
        {
            protected override View Body => Span["Hello B"];
        }
        """;

    private const string ComponentBModifiedSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public partial class ComponentB : BodyComponentBase
        {
            protected override View Body => Span["Modified B"];
        }
        """;

    /// <summary>
    /// Proves that when the same driver is reused and only one syntax tree changes,
    /// the pipeline caches the unchanged component model and recomputes the changed one.
    /// Identifies each component by its <see cref="ComponentModel"/> value.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_WhenOnlyOtherTreeChanges_CachesUnchangedComponent()
    {
        // Arrange: two components in separate syntax trees
        var treeA = CSharpSyntaxTree.ParseText(
            ComponentASource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentA.cs");

        var treeB = CSharpSyntaxTree.ParseText(
            ComponentBSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentB.cs");

        var compilation = CreateCompilation(treeA, treeB);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
            driverOptions: driverOptions);

        // Run 1: initial full run
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run1 = driver.GetRunResult();
        Assert.Equal(2, run1.Results[0].GeneratedSources.Length);

        // Act: replace only tree B with a modified version
        var treeBModified = CSharpSyntaxTree.ParseText(
            ComponentBModifiedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentB.cs");

        var compilation2 = compilation.ReplaceSyntaxTree(treeB, treeBModified);

        // Run 2: reuse the same driver returned from Run 1
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
        var run2 = driver.GetRunResult();

        // Assert: the pipeline must have tracked steps
        var trackedSteps = run2.Results[0].TrackedSteps;
        Assert.True(trackedSteps.ContainsKey("ComponentModeling"),
            "Expected tracked step 'ComponentModeling' but found: " +
            string.Join(", ", trackedSteps.Keys));

        // The ComponentModeling step should have two outputs (one per component)
        var modelingSteps = trackedSteps["ComponentModeling"];
        var allOutputs = modelingSteps.SelectMany(s => s.Outputs).ToImmutableArray();
        Assert.Equal(2, allOutputs.Length);

        // Identify each output by its ComponentModelResult's model value
        var componentA = allOutputs.Single(o =>
            o.Value is ComponentModelResult result && result.Model is { } model && model.ClassName == "ComponentA");
        var componentB = allOutputs.Single(o =>
            o.Value is ComponentModelResult result && result.Model is { } model && model.ClassName == "ComponentB");

        // ComponentA is unchanged → Cached or Unchanged
        Assert.True(
            componentA.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected ComponentA to be Cached/Unchanged but got {componentA.Reason}");

        // ComponentB was modified → Modified or New
        Assert.True(
            componentB.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New,
            $"Expected ComponentB to be Modified/New but got {componentB.Reason}");
    }

    /// <summary>
    /// Proves that when nothing changes between runs, all component models are cached.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_WhenNothingChanges_CachesAllComponents()
    {
        var treeA = CSharpSyntaxTree.ParseText(
            ComponentASource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentA.cs");

        var treeB = CSharpSyntaxTree.ParseText(
            ComponentBSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentB.cs");

        var compilation = CreateCompilation(treeA, treeB);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
            driverOptions: driverOptions);

        // Run 1
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        // Run 2 with the same compilation (no changes)
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run2 = driver.GetRunResult();

        var trackedSteps = run2.Results[0].TrackedSteps;
        Assert.True(trackedSteps.ContainsKey("ComponentModeling"),
            "Expected tracked step 'ComponentModeling'");

        var modelingSteps = trackedSteps["ComponentModeling"];
        var allOutputs = modelingSteps.SelectMany(s => s.Outputs).ToImmutableArray();

        // Both should be cached/unchanged when nothing changed
        Assert.All(allOutputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected Cached or Unchanged but got {output.Reason}"));
    }

    /// <summary>
    /// Regression test: changing the <c>BlazorCodeFirst.Html</c> API signature between reused-driver
    /// runs must invalidate all component models (not incorrectly cache them). This validates
    /// that <c>KnownSymbols</c> equality is based on a semantic signature fingerprint, not mere
    /// symbol presence.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_WhenHtmlApiSignatureChanges_InvalidatesAllComponentModels()
    {
        // Source-defined Html type so we can mutate its signature between runs.
        const string runtimeSourceV1 = """
            namespace BlazorCodeFirst
            {
                public struct View
                {
                    public static implicit operator View(string text) => default;
                }
                public readonly struct ElementView
                {
                    public View this[params System.ReadOnlySpan<View> children] => default;
                    public static implicit operator View(ElementView builder) => default;
                }
                public abstract class BodyComponentBase : Microsoft.AspNetCore.Components.ComponentBase
                {
                    protected abstract View Body { get; }
                }
                public static class Html
                {
                    public static ElementView Span => default;
                }
            }
            """;

        const string runtimeSourceV2 = """
            namespace BlazorCodeFirst
            {
                public struct View
                {
                    public static implicit operator View(string text) => default;
                }
                public readonly struct ElementView
                {
                    public View this[params System.ReadOnlySpan<View> children] => default;
                    public static implicit operator View(ElementView builder) => default;
                }
                public abstract class BodyComponentBase : Microsoft.AspNetCore.Components.ComponentBase
                {
                    protected abstract View Body { get; }
                }
                public static class Html
                {
                    // The curated-tag match requires the property's type to be ElementView (KnownSymbols
                    // checks both the name and the type), so retyping it to View is a real signature change:
                    // Span still exists, but no longer resolves as a curated element.
                    public static View Span => default;
                }
            }
            """;

        const string componentSource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace TestNs;

            public partial class MyComponent : BodyComponentBase
            {
                protected override View Body => Span["Hello"];
            }
            """;

        var runtimeTreeV1 = CSharpSyntaxTree.ParseText(
            runtimeSourceV1,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Runtime.cs");

        var componentTree = CSharpSyntaxTree.ParseText(
            componentSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "MyComponent.cs");

        // Build compilation WITHOUT the real BlazorCodeFirst.Runtime assembly reference-
        // use our in-source definitions instead.
        var compilation1 = CreateCompilationWithoutRuntime(runtimeTreeV1, componentTree);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
            driverOptions: driverOptions);

        // Run 1: Span resolves as a curated ElementView property, component is generated
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _);
        var run1 = driver.GetRunResult();
        Assert.Single(run1.Results[0].GeneratedSources);

        // Act: replace the runtime tree with V2 (Span is retyped from ElementView to View)
        var runtimeTreeV2 = CSharpSyntaxTree.ParseText(
            runtimeSourceV2,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Runtime.cs");

        var compilation2 = compilation1.ReplaceSyntaxTree(runtimeTreeV1, runtimeTreeV2);

        // Run 2: reuse driver with changed Html API
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
        var run2 = driver.GetRunResult();

        // The Html API changed, so the syntax-provider transform must re-analyze each candidate against
        // the new compilation (resolving BlazorCodeFirst.Html symbols transiently) and the downstream
        // pipeline must NOT incorrectly cache the old component model.
        var trackedSteps = run2.Results[0].TrackedSteps;

        // Verify the component analysis was recomputed (Modified or New): Span no longer resolves as
        // a curated element, so the analyzed template changes to a model-less result.
        Assert.True(trackedSteps.ContainsKey("ComponentAnalysis"),
            "Expected tracked step 'ComponentAnalysis'");
        var analysisOutputs = trackedSteps["ComponentAnalysis"]
            .SelectMany(s => s.Outputs).ToImmutableArray();
        Assert.All(analysisOutputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Modified or IncrementalStepRunReason.New,
                $"Expected ComponentAnalysis Modified/New but got {output.Reason}"));

        // The component model must NOT be reused (Cached) in Run 2 because Span no longer matches
        // (its type changed from ElementView to View); the regression this guards against is a
        // stale Cached reuse of the old model built against the previous Html API.
        if (trackedSteps.TryGetValue("ComponentModeling", out var modelingSteps))
        {
            var modelOutputs = modelingSteps.SelectMany(s => s.Outputs).ToImmutableArray();
            Assert.All(modelOutputs, output =>
                Assert.True(
                    output.Reason is not IncrementalStepRunReason.Cached,
                    $"ComponentModel was incorrectly cached after Html API signature change (reason: {output.Reason})"));
        }

        // The second run should produce NO generated sources because Span no longer resolves as a
        // known curated element.
        Assert.Empty(run2.Results[0].GeneratedSources);
    }

    [Fact]
    public void IncrementalGenerator_WhenUnrelatedTreeChanges_KeepsGeneratingComponent()
    {
        // Regression guard for symbol provenance: editing an unrelated tree produces a new compilation.
        // The component's Body analysis must re-resolve BlazorCodeFirst.Html from that new compilation rather
        // than reusing symbols from the previous one; otherwise a cross-compilation symbol comparison would
        // silently stop recognizing Div/Span/Button/If and drop the generated RenderView on every
        // incremental edit (visible under dotnet watch / the IDE, not a single-shot build).
        const string componentSource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace TestNs;

            public partial class MyComponent : BodyComponentBase
            {
                protected override View Body => Span["Hello"];
            }
            """;

        const string unrelatedV1 = """
            namespace Other;

            public class Unrelated
            {
                public int Value => 1;
            }
            """;

        const string unrelatedV2 = """
            namespace Other;

            public class Unrelated
            {
                public int Value => 2;
            }
            """;

        const string runtimeSource = """
            namespace BlazorCodeFirst
            {
                public struct View
                {
                    public static implicit operator View(string text) => default;
                }
                public readonly struct ElementView
                {
                    public View this[params System.ReadOnlySpan<View> children] => default;
                    public static implicit operator View(ElementView builder) => default;
                }
                public abstract class BodyComponentBase : Microsoft.AspNetCore.Components.ComponentBase
                {
                    protected abstract View Body { get; }
                }
                public static class Html
                {
                    public static ElementView Span => default;
                }
            }
            """;

        var runtimeTree = CSharpSyntaxTree.ParseText(
            runtimeSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Runtime.cs");
        var componentTree = CSharpSyntaxTree.ParseText(
            componentSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "MyComponent.cs");
        var unrelatedTreeV1 = CSharpSyntaxTree.ParseText(
            unrelatedV1,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Unrelated.cs");

        var compilation1 = CreateCompilationWithoutRuntime(runtimeTree, componentTree, unrelatedTreeV1);

        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
            driverOptions: driverOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation1, out _, out _);
        var run1Source = Assert.Single(driver.GetRunResult().Results[0].GeneratedSources);

        // Edit only the unrelated tree.
        var unrelatedTreeV2 = CSharpSyntaxTree.ParseText(
            unrelatedV2,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Unrelated.cs");
        var compilation2 = compilation1.ReplaceSyntaxTree(unrelatedTreeV1, unrelatedTreeV2);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
        var run2 = driver.GetRunResult();

        // The component must still be generated, with identical output, after the unrelated edit.
        var run2Source = Assert.Single(run2.Results[0].GeneratedSources);
        Assert.Equal(
            run1Source.SourceText.ToString(),
            run2Source.SourceText.ToString());

        // Its model was reused, not recomputed to a different value.
        if (run2.Results[0].TrackedSteps.TryGetValue("ComponentModeling", out var modelingSteps))
        {
            var reasons = modelingSteps.SelectMany(s => s.Outputs).Select(o => o.Reason).ToImmutableArray();
            Assert.All(reasons, reason =>
                Assert.True(
                    reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Expected component model reuse after unrelated edit but got {reason}"));
        }
    }

    // ---------------------------------------------------------------------------
    // Cross-file view part invalidation and registry stability
    // ---------------------------------------------------------------------------

    private const string WidgetsSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public static class Widgets
        {
            [ViewPart]
            public static View Label(string value) => Span[value];
        }
        """;

    private const string BadgesSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public static class Badges
        {
            [ViewPart]
            public static View Badge(string value) => Span["[" + value + "]"];
        }
        """;

    private const string WidgetsModifiedSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public static class Widgets
        {
            [ViewPart]
            public static View Label(string value) => Span[value + "!"];
        }
        """;

    private const string CallerSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public partial class Caller : BodyComponentBase
        {
            protected override View Body => Widgets.Label("x");
        }
        """;

    private const string UnrelatedSource = """
        using BlazorCodeFirst;
        using static BlazorCodeFirst.Html;

        namespace TestNs;

        public partial class Unrelated : BodyComponentBase
        {
            protected override View Body => Span["z"];
        }
        """;

    /// <summary>
    /// Changing a view part definition file must recompute the caller that expands it (Modified) while
    /// the unrelated component that never calls it recomputes to an equal model (Unchanged/Cached).
    /// </summary>
    [Fact]
    public void IncrementalGenerator_WhenViewPartDefinitionChanges_InvalidatesOnlyDependentCaller()
    {
        var widgetsTree = ParseTree(WidgetsSource, "Widgets.cs");
        var callerTree = ParseTree(CallerSource, "Caller.cs");
        var unrelatedTree = ParseTree(UnrelatedSource, "Unrelated.cs");

        var compilation = CreateCompilation(widgetsTree, callerTree, unrelatedTree);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var widgetsModified = ParseTree(WidgetsModifiedSource, "Widgets.cs");
        var compilation2 = compilation.ReplaceSyntaxTree(widgetsTree, widgetsModified);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation2, out _, out _);
        var run2 = driver.GetRunResult();

        var outputs = run2.Results[0].TrackedSteps["ComponentModeling"]
            .SelectMany(s => s.Outputs).ToImmutableArray();

        var callerOutput = outputs.Single(o =>
            o.Value is ComponentModelResult result && result.Model is { } model && model.ClassName == "Caller");
        var unrelatedOutput = outputs.Single(o =>
            o.Value is ComponentModelResult result && result.Model is { } model && model.ClassName == "Unrelated");

        Assert.Equal(IncrementalStepRunReason.Modified, callerOutput.Reason);
        Assert.True(
            unrelatedOutput.Reason is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached,
            $"Expected Unrelated to be Unchanged/Cached but got {unrelatedOutput.Reason}");
    }

    /// <summary>
    /// An identical rerun with the same compilation must reuse the view part registry (Cached/Unchanged)
    /// rather than rebuilding a distinct-but-equal value.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_OnIdenticalRerun_CachesViewPartRegistry()
    {
        var widgetsTree = ParseTree(WidgetsSource, "Widgets.cs");
        var callerTree = ParseTree(CallerSource, "Caller.cs");

        var compilation = CreateCompilation(widgetsTree, callerTree);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run2 = driver.GetRunResult();

        var registryOutputs = run2.Results[0].TrackedSteps["ViewPartRegistry"]
            .SelectMany(s => s.Outputs).ToImmutableArray();

        Assert.All(registryOutputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected ViewPartRegistry Cached/Unchanged but got {output.Reason}"));
    }

    /// <summary>
    /// An identical rerun with the same compilation must reuse the view part ForEach diagnostics output
    /// (Cached/Unchanged) rather than recomputing a distinct-but-equal <see cref="EquatableArray{T}"/>.
    /// This guards the <c>(EquatableArray&lt;DiagnosticInfo&gt;)</c> cast on the "ViewPartForEachDiagnostics"
    /// step in <c>BlazorCodeFirstGenerator</c>: if that cast were reverted to a raw
    /// <see cref="ImmutableArray{T}"/>, the step's output would compare by underlying-array reference
    /// instead of by structural value, so this identical rerun would report Modified instead of
    /// Cached/Unchanged and this test would fail.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_OnIdenticalRerun_CachesViewPartForEachDiagnostics()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCodeFirst.Html;
            public static class Widgets
            {
                [BlazorCodeFirst.ViewPart]
                public static BlazorCodeFirst.View Never(List<Group> gs) =>
                    ForEach(gs, key: g => g.Id, content: g =>
                        ForEach(g.Items, key: i => i.Id, content: i => Span[i.Name]));
                public sealed record Item(int Id, string Name);
                public sealed record Group(int Id, List<Item> Items);
            }
            """;

        var tree = ParseTree(source, "Widgets.cs");
        var compilation = CreateCompilation(tree);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run2 = driver.GetRunResult();

        var diagnosticsOutputs = run2.Results[0].TrackedSteps["ViewPartForEachDiagnostics"]
            .SelectMany(s => s.Outputs).ToImmutableArray();

        // Sanity check: the step must have actually produced the BCF3003 diagnostic (nested ForEach with
        // a region-rooted content root), not an empty array, otherwise this test would trivially pass
        // without ever exercising the EquatableArray value-equality path.
        Assert.Contains(diagnosticsOutputs, output =>
            output.Value is EquatableArray<DiagnosticInfo> diagnostics &&
            diagnostics.AsImmutableArray().Any(d => d.Id == "BCF3003"));

        Assert.All(diagnosticsOutputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected ViewPartForEachDiagnostics Cached/Unchanged but got {output.Reason}"));
    }

    /// <summary>
    /// Reordering syntax trees without changing any view part definition must yield an equal registry,
    /// proving equality is by sorted value rather than discovery order or ImmutableArray reference.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_WhenSyntaxTreesAreReordered_ProducesEqualRegistry()
    {
        var registryForward = ExtractRegistry(
            ParseTree(WidgetsSource, "Widgets.cs"),
            ParseTree(BadgesSource, "Badges.cs"),
            ParseTree(CallerSource, "Caller.cs"));

        var registryReversed = ExtractRegistry(
            ParseTree(CallerSource, "Caller.cs"),
            ParseTree(BadgesSource, "Badges.cs"),
            ParseTree(WidgetsSource, "Widgets.cs"));

        Assert.Equal(registryForward, registryReversed);
    }

    /// <summary>
    /// The diagnostic-only branch must also participate in caching: an identical rerun of a component
    /// that produces a BCF1002 must report the modeling output as Cached/Unchanged, not Modified merely
    /// because a fresh diagnostic value was allocated.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_OnIdenticalRerun_CachesDiagnosticOnlyModelResult()
    {
        const string cyclicSource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace TestNs;

            public partial class Cyclic : BodyComponentBase
            {
                [ViewPart]
                private static View Loop() => Loop();

                protected override View Body => Loop();
            }
            """;

        var tree = ParseTree(cyclicSource, "Cyclic.cs");
        var compilation = CreateCompilation(tree);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run2 = driver.GetRunResult();

        var outputs = run2.Results[0].TrackedSteps["ComponentModeling"]
            .SelectMany(s => s.Outputs).ToImmutableArray();

        var cyclic = outputs.Single(o =>
            o.Value is ComponentModelResult result && !result.Diagnostics.IsDefaultOrEmpty);

        Assert.True(
            cyclic.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected diagnostic-only result Cached/Unchanged but got {cyclic.Reason}");
    }

    /// <summary>
    /// Proves that the <c>Component&lt;T&gt;()</c> interop model (<see cref="ComponentNode"/>,
    /// <see cref="ComponentTemplateNode"/>, and their <c>EquatableArray&lt;ComponentParameter&gt;</c>
    /// parameter lists) is value-equal across identical reruns, so the host's
    /// <c>ComponentModeling</c> output is cached rather than recomputed as a distinct-but-equal value.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_OnIdenticalRerun_CachesComponentInteropModel()
    {
        const string childSource = """
            using Microsoft.AspNetCore.Components;
            namespace TestNs;
            public class Child : ComponentBase { [Parameter] public string Label { get; set; } = ""; }
            """;
        const string hostSource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace TestNs;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Child>().Param(c => c.Label, "hi");
            }
            """;

        var compilation = CreateCompilation(
            ParseTree(childSource, "Child.cs"),
            ParseTree(hostSource, "Host.cs"));
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run2 = driver.GetRunResult();

        var outputs = run2.Results[0].TrackedSteps["ComponentModeling"]
            .SelectMany(s => s.Outputs).ToImmutableArray();
        var host = outputs.Single(o =>
            o.Value is ComponentModelResult result && result.Model is { } model && model.ClassName == "Host");

        Assert.True(
            host.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected Component<T> host model reuse but got {host.Reason}");
    }

    /// <summary>
    /// A generic component's model must be reused across identical reruns. This guards
    /// <c>ComponentAnalysis.TypeParameters</c> / <c>ComponentModel.TypeParameters</c> being an
    /// <see cref="EquatableArray{T}"/>: as a raw <see cref="ImmutableArray{T}"/> the record's synthesized
    /// equality would compare the type-parameter names by underlying-array reference, so every generic
    /// component would recompute as Modified on every incremental run. Every other incrementality test
    /// uses a non-generic component, where the array is empty and the defect is invisible.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_OnIdenticalRerun_CachesGenericComponentModel()
    {
        const string genericSource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace TestNs;

            public partial class Gen<TItem> : BodyComponentBase
            {
                protected override View Body => Span["g"];
            }
            """;

        var compilation = CreateCompilation(ParseTree(genericSource, "Gen.cs"));
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run2 = driver.GetRunResult();

        var outputs = run2.Results[0].TrackedSteps["ComponentModeling"]
            .SelectMany(s => s.Outputs).ToImmutableArray();

        // Sanity check: the step must have produced the GENERIC model, otherwise the assertion below
        // would pass without ever exercising the type-parameter equality path.
        Assert.Contains(outputs, output =>
            output.Value is ComponentModelResult { Model: { } model } &&
            model.TypeParameters.Length == 1);

        Assert.All(outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected generic component model reuse but got {output.Reason}"));
    }

    /// <summary>
    /// A component with slot children must be reused across identical reruns. This guards the nested node
    /// tree inside <c>EquatableArray</c> on the slot channel path (ComponentNode.Children, ComponentSlot):
    /// if any layer of that tree compared by reference instead of structurally, every component with child
    /// content would recompute as Modified on every generator run, and that would be invisible to every
    /// other test in the suite.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_OnIdenticalRerun_CachesComponentSlotModel()
    {
        // A slot carries a nested node tree inside EquatableArray. If any layer of that tree compared by
        // reference, every component with child content would recompute as Modified on each run.
        const string card = """
            using Microsoft.AspNetCore.Components;
            namespace T;
            public class Card : ComponentBase
            {
                [Parameter] public RenderFragment? ChildContent { get; set; }
            }
            """;
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Card>()[Div["x"], "text"];
            }
            """;

        var compilation = CreateCompilation(
            ParseTree(card, "Card.cs"),
            ParseTree(host, "Host.cs"));
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var run2 = driver.GetRunResult();

        var outputs = run2.Results[0].TrackedSteps["ComponentModeling"]
            .SelectMany(s => s.Outputs).ToImmutableArray();

        Assert.All(outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected Cached or Unchanged but got {output.Reason}"));
    }

    [Fact]
    public void IncrementalGenerator_OnIdenticalRerun_CachesGenericTemplateContextualSlotModel()
    {
        const string target = """
            using Microsoft.AspNetCore.Components;
            namespace T;
            public class Target : ComponentBase
            {
                [Parameter] public RenderFragment<int>? RowTemplate { get; set; }
            }
            """;
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Build();

                [ViewPart]
                private static View Build() =>
                    Div[Fragment(If(true, () =>
                        Component<Target>().Template(
                            c => c.RowTemplate,
                            context => Span[context.ToString()])))];
            }
            """;

        var compilation = CreateCompilation(
            ParseTree(target, "Target.cs"),
            ParseTree(host, "Host.cs"));
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var contextualOutputs = driver.GetRunResult().Results[0].TrackedSteps["ComponentModeling"]
            .SelectMany(static step => step.Outputs)
            .Where(static output =>
                output.Value is ComponentModelResult { Model.RootNode: { } root }
                && ContainsContextualSlot(root))
            .ToImmutableArray();

        Assert.NotEmpty(contextualOutputs);
        Assert.All(contextualOutputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected contextual generic-template model reuse but got {output.Reason}"));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static SyntaxTree ParseTree(string source, string path) =>
        CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: path);

    private static CSharpGeneratorDriver CreateDriver() =>
        (CSharpGeneratorDriver)CSharpGeneratorDriver.Create(
            generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: default,
                trackIncrementalGeneratorSteps: true));

    private static bool ContainsContextualSlot(RenderNode node) =>
        node switch
        {
            ComponentNode component => component.Slots.Any(static slot =>
                slot.Kind == ComponentSlotKind.GenericContextual
                || ContainsContextualSlot(slot.Content)),
            ElementNode element => element.Children.Any(ContainsContextualSlot),
            FragmentNode fragment => fragment.Children.Any(ContainsContextualSlot),
            IfNode conditional => ContainsContextualSlot(conditional.Then)
                || conditional.Otherwise is not null
                && ContainsContextualSlot(conditional.Otherwise),
            ForEachNode forEach => ContainsContextualSlot(forEach.Content),
            ExpansionNode expansion => ContainsContextualSlot(expansion.Body),
            _ => false,
        };

    /// <summary>
    /// A component that translates cleanly must stay cached when an edit shifts its absolute offsets
    /// without changing it. <see cref="ComponentAnalysis.FailureLocation"/> is the coordinate-bearing
    /// field this could regress through: it exists to locate BCF1003 (#77) and is populated only on the
    /// failure path, so a healthy component must keep contributing nothing to the cache key. Move that
    /// assignment out of its <c>template is null</c> guard and every component in the compilation becomes
    /// sensitive to a blank line inserted anywhere above it.
    /// </summary>
    [Fact]
    public void IncrementalGenerator_WhenAnEditOnlyShiftsOffsets_CachesTheTranslatedComponent()
    {
        // Deliberately a body with no ForEach and no [ViewPart] call: those template nodes carry a
        // TemplateLocation of their own, which is a separate and intended source of offset sensitivity.
        var before = CSharpSyntaxTree.ParseText(
            ComponentASource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentA.cs");

        var after = CSharpSyntaxTree.ParseText(
            "\n" + ComponentASource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "ComponentA.cs");

        var compilation = CreateCompilation(before);

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation.ReplaceSyntaxTree(before, after), out _, out _);

        // Asserted on ComponentAnalysis, not ComponentModeling: the edited tree forces the syntax
        // transform to re-run either way, so what is being tested is whether the value it produces is
        // equal. Downstream, an unequal ComponentAnalysis that still expands to an equal model reports
        // Unchanged rather than Modified, which would hide exactly the regression this guards.
        var outputs = driver.GetRunResult().Results[0].TrackedSteps["ComponentAnalysis"]
            .SelectMany(static step => step.Outputs)
            .ToImmutableArray();

        Assert.NotEmpty(outputs);
        Assert.All(outputs, output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected Cached/Unchanged after an offset-only edit but got {output.Reason}"));
    }

    private static ViewPartRegistry ExtractRegistry(params SyntaxTree[] trees)
    {
        var compilation = CreateCompilation(trees);
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        var output = driver.GetRunResult().Results[0].TrackedSteps["ViewPartRegistry"]
            .SelectMany(s => s.Outputs)
            .Single();
        return (ViewPartRegistry)output.Value!;
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees)
    {
        var references = CompilationTestHost.BuildMetadataReferences();
        return CSharpCompilation.Create(
            assemblyName: "IncrementalTestAssembly",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static CSharpCompilation CreateCompilationWithoutRuntime(params SyntaxTree[] trees)
    {
        // Only ComponentBase, NOT the BlazorCodeFirst.Runtime assembly, since these trees declare the
        // BlazorCodeFirst types in-source. BuildMetadataReferences must filter the runtime out of the trusted
        // platform assemblies to achieve that: this project references the runtime, so it is in TPA and an
        // earlier version of this comment claimed an isolation the conditional Add alone never provided.
        var references = CompilationTestHost.BuildMetadataReferences(includeRuntime: false);
        return CSharpCompilation.Create(
            assemblyName: "IncrementalTestAssembly",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

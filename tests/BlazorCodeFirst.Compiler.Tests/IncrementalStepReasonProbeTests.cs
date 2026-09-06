using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Establishes, empirically, the one mechanism issue #480's registry-subset alternative depends on:
/// when an <see cref="IncrementalGeneratorInitializationContext"/> stage re-runs and returns a value
/// equal to its previous output (<see cref="IncrementalStepRunReason.Unchanged"/>), a downstream
/// <c>Select</c> consuming only that value is skipped and reports
/// <see cref="IncrementalStepRunReason.Cached"/> rather than re-invoking its transform.
/// </summary>
/// <remarks>
/// No existing test in this project proves this. Every reason assertion in
/// <see cref="IncrementalGeneratorTests"/> is either a single-step check or a
/// <c>Cached or Unchanged</c> disjunction, so none of them can distinguish "the transform re-ran and
/// happened to return an equal value" from "the transform never ran at all". If this probe ever
/// starts failing, the registry-subset alternative floated in #480 is void: the projection stage
/// would still run once per component on every registry edit, and <c>Expand</c> would still re-run
/// downstream of it, buying nothing over the current whole-registry <c>Combine</c>.
/// </remarks>
public sealed class IncrementalStepReasonProbeTests
{
    /// <summary>
    /// Decisive form: a synthetic two-stage generator counts stage-2 invocations directly, so there is
    /// no need to interpret <see cref="IncrementalStepRunReason"/> as a proxy for "did the transform
    /// run". <see cref="IncrementalGeneratorInitializationContext.CompilationProvider"/> is guaranteed
    /// to re-run on every driver run (a new <see cref="Compilation"/> object each time), so stage 1
    /// re-running with an equal value needs no dependence on syntax-tree caching internals.
    /// </summary>
    [Fact]
    public void SyntheticTwoStageGenerator_WhenStageOneRerunsWithEqualValue_SkipsStageTwo()
    {
        var generator = new TwoStageProbeGenerator();
        var driver = (CSharpGeneratorDriver)CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(
                disabledOutputs: default,
                trackIncrementalGeneratorSteps: true));

        var before = CSharpSyntaxTree.ParseText(
            "// probe",
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Probe.cs");
        var after = CSharpSyntaxTree.ParseText(
            "// probe\n",
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
            path: "Probe.cs");

        var compilation = CSharpCompilation.Create(
            assemblyName: "ProbeAssembly",
            syntaxTrees: [before],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation.ReplaceSyntaxTree(before, after), out _, out _);

        var result = driver.GetRunResult().Results[0];
        var stage1 = result.TrackedSteps["Stage1"].Single().Outputs.Single();
        var stage2 = result.TrackedSteps["Stage2"].Single().Outputs.Single();

        // Ground truth first: the counter is what actually matters. A step's reported Reason is only
        // a proxy for it, and this assertion cannot be satisfied by an implementation that re-runs
        // stage 2 but happens to report a misleading reason.
        Assert.Equal(1, generator.Stage2Invocations);

        Assert.Equal(IncrementalStepRunReason.Unchanged, stage1.Reason);
        Assert.Equal(IncrementalStepRunReason.Cached, stage2.Reason);
    }

    /// <summary>
    /// Same proposition against the real <see cref="BlazorCodeFirstGenerator"/> pipeline, using the
    /// registry-broadcast corpus shape from <c>RegistryBroadcastCostTests</c>: an offset-only edit
    /// (a leading blank line) to a <c>ForEach</c>-bodied <c>[ViewPart]</c> file, which genuinely
    /// changes <see cref="ViewPartRegistry"/> (its entries carry a <c>TemplateLocation</c>) while
    /// leaving a non-calling component's emitted model unchanged.
    /// </summary>
    /// <remarks>
    /// Unlike the synthetic probe above, this assertion is still the <c>Cached or Unchanged</c>
    /// disjunction every other test in this project uses — it does not by itself distinguish "did not
    /// run" from "re-ran and returned an equal value" the way counting <c>Stage2Invocations</c> does.
    /// It earns its place anyway: it confirms the guard (the edit must genuinely change
    /// <see cref="ViewPartRegistry"/>, or the scenario is vacuous) in the real pipeline shape #480's
    /// design actually takes, and its outcome is consistent with the mechanism the synthetic probe
    /// proves outright.
    /// </remarks>
    [Fact]
    public void RealPipeline_WhenAnUnrelatedViewPartOffsetEditLeavesTheModelEqual_CachesTheNonCaller()
    {
        const string supportSource = """
            namespace TestNs;

            public sealed record Item(int Id, string Name);
            """;

        const string widgetSource = """
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
            """;

        const string callerSource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace TestNs;

            public partial class Caller : BodyComponentBase
            {
                protected override View Body => Widgets.Widget(new());
            }
            """;

        const string nonCallerSource = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;

            namespace TestNs;

            public partial class NonCaller : BodyComponentBase
            {
                protected override View Body => Span["Unrelated"];
            }
            """;

        var supportTree = ParseTree(supportSource, "Support.cs");
        var widgetBefore = ParseTree(widgetSource, "Widget.cs");
        var widgetAfter = ParseTree("\n" + widgetSource, "Widget.cs");
        var callerTree = ParseTree(callerSource, "Caller.cs");
        var nonCallerTree = ParseTree(nonCallerSource, "NonCaller.cs");

        var compilation = CreateCompilation(supportTree, widgetBefore, callerTree, nonCallerTree);

        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation.ReplaceSyntaxTree(widgetBefore, widgetAfter), out _, out _);

        var result = driver.GetRunResult().Results[0];

        // Guard: without this, a passing test below would prove nothing — the registry might not
        // have changed at all.
        var registryOutputs = result.TrackedSteps["ViewPartRegistry"].SelectMany(static s => s.Outputs);
        Assert.Contains(registryOutputs, static o => o.Reason == IncrementalStepRunReason.Modified);

        var modelingOutputs = result.TrackedSteps["ComponentModeling"]
            .SelectMany(static s => s.Outputs)
            .ToImmutableArray();

        Assert.Equal(2, modelingOutputs.Length);
        Assert.All(modelingOutputs, static output =>
            Assert.True(
                output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                $"Expected Cached/Unchanged after an unrelated view part offset edit but got {output.Reason}"));
    }

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

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] trees) =>
        CSharpCompilation.Create(
            assemblyName: "IncrementalStepReasonProbeAssembly",
            syntaxTrees: trees,
            references: CompilationTestHost.BuildMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// Stage 2 depends only on stage 1's value (a plain <c>int</c>), so if Roslyn's incremental
    /// pipeline skips re-invocation on an equal upstream value, <see cref="Stage2Invocations"/> stays
    /// at 1 across both driver runs even though stage 1 itself runs twice.
    /// </summary>
    private sealed class TwoStageProbeGenerator : IIncrementalGenerator
    {
        public int Stage2Invocations { get; private set; }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var stage1 = context.CompilationProvider
                .Select(static (compilation, _) => compilation.SyntaxTrees.Count())
                .WithTrackingName("Stage1");

            var stage2 = stage1
                .Select((count, _) =>
                {
                    Stage2Invocations++;
                    return count * 2;
                })
                .WithTrackingName("Stage2");

            context.RegisterSourceOutput(
                stage2,
                static (spc, value) => spc.AddSource("Probe.g.cs", $"// {value}"));
        }
    }
}

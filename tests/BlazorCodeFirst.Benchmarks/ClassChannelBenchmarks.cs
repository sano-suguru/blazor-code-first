using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BlazorCodeFirst.Benchmarks.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.Benchmarks;

/// <summary>
/// What the class channel's join costs, in the two questions that have been asked of it. The
/// <see cref="Rule"/> rows are #236's: what the join allocates per build, run before and after a change
/// to the generation rule, where the change is only worth taking if no shape allocates more than it
/// did. The <see cref="Site"/> rows are #239's: whether that join is cheaper written into each
/// generated class or as one span-taking method on the runtime assembly.
/// </summary>
/// <remarks>
/// Not a <c>DESIGN.md</c> §7.1 figure. §7.1 publishes a comparison against an equivalent Razor
/// component, gated by <c>Program.Main</c> on frame equivalence, and there is no Razor spelling of an
/// additive class channel to put on the other side. These numbers answer narrower questions — whether
/// a generation rule got more expensive than the one it replaces, and where the join belongs — and
/// live in #236 and #239.
/// <para>
/// The two sets measure different things and are grouped so their ratios stay separate. #236's rows
/// build a whole element through the generator, because what it asked was what a render costs. #239's
/// rows call the join alone, because everything around it is identical between its two candidates and
/// including it would only add the frame calls' variance (see <see cref="ClassJoinCandidates"/>).
/// </para>
/// </remarks>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ClassChannelBenchmarks
{
    /// <summary>#236: what a build allocates under the generation rule the emitter uses.</summary>
    private const string Rule = "generation rule";

    /// <summary>#239: what the join costs where it is written, against where it could be written.</summary>
    private const string Site = "join site";

    private readonly ClassJoinNonNullView _nonNull = new();
    private readonly ClassJoinNullView _null = new();
    private readonly ClassJoinThreeView _three = new();
    private readonly RenderTreeBuilder _builder = new();

    /// <summary>
    /// The terms the <see cref="Site"/> rows join. Fields filled in <see cref="Setup"/> rather than
    /// constants or initializers, so the join has a value it cannot read at JIT time — which is the
    /// position a class decoration is in, its value coming from a field or a property of the component.
    /// </summary>
    private string? _card;
    private string? _variant;
    private string? _size;
    private string? _state;
    private string? _absent;

    [GlobalSetup]
    public void Setup()
    {
        _card = "card";
        _variant = "wide";
        _size = "lg";
        _state = "is-open";
        _absent = null;
    }

    [GlobalCleanup]
    public void Cleanup() => _builder.Dispose();

    /// <summary>Two terms, neither null. The row that decided #236.</summary>
    [Benchmark(Baseline = true, Description = "class join: two non-null terms")]
    [BenchmarkCategory(Rule)]
    public void Join_TwoNonNullTerms()
    {
        _builder.Clear();
        _nonNull.Build(_builder);
    }

    /// <summary>Two terms, the second null at render time.</summary>
    [Benchmark(Description = "class join: second term null")]
    [BenchmarkCategory(Rule)]
    public void Join_SecondTermNull()
    {
        _builder.Clear();
        _null.Build(_builder);
    }

    /// <summary>Three terms, none null.</summary>
    [Benchmark(Description = "class join: three non-null terms")]
    [BenchmarkCategory(Rule)]
    public void Join_ThreeNonNullTerms()
    {
        _builder.Clear();
        _three.Build(_builder);
    }

    /// <summary>Two terms, neither null: the arity the channel is written at most often.</summary>
    [Benchmark(Baseline = true, Description = "site: generated class, two terms")]
    [BenchmarkCategory(Site)]
    public string? Site_GeneratedTwo() => ClassJoinCandidates.Generated(_card, _variant);

    /// <summary>The same two terms through the runtime method.</summary>
    [Benchmark(Description = "site: runtime method, two terms")]
    [BenchmarkCategory(Site)]
    public string? Site_RuntimeTwo() => ClassJoinCandidates.Runtime(_card, _variant);

    /// <summary>Two terms, the second null: one arm of the ladder, one skipped write of the buffer.</summary>
    [Benchmark(Description = "site: generated class, two terms, second null")]
    [BenchmarkCategory(Site)]
    public string? Site_GeneratedTwoWithNull() => ClassJoinCandidates.Generated(_card, _absent);

    /// <summary>The same shape through the runtime method.</summary>
    [Benchmark(Description = "site: runtime method, two terms, second null")]
    [BenchmarkCategory(Site)]
    public string? Site_RuntimeTwoWithNull() => ClassJoinCandidates.Runtime(_card, _absent);

    /// <summary>Three terms, none null: where the generated concatenation first reaches a span.</summary>
    [Benchmark(Description = "site: generated class, three terms")]
    [BenchmarkCategory(Site)]
    public string? Site_GeneratedThree() => ClassJoinCandidates.Generated(_card, _variant, _size);

    /// <summary>The same three terms through the runtime method.</summary>
    [Benchmark(Description = "site: runtime method, three terms")]
    [BenchmarkCategory(Site)]
    public string? Site_RuntimeThree() => ClassJoinCandidates.Runtime(_card, _variant, _size);

    /// <summary>Four terms, none null: the widest ladder the generated class writes here.</summary>
    [Benchmark(Description = "site: generated class, four terms")]
    [BenchmarkCategory(Site)]
    public string? Site_GeneratedFour() =>
        ClassJoinCandidates.Generated(_card, _variant, _size, _state);

    /// <summary>The same four terms through the runtime method.</summary>
    [Benchmark(Description = "site: runtime method, four terms")]
    [BenchmarkCategory(Site)]
    public string? Site_RuntimeFour() => ClassJoinCandidates.Runtime(_card, _variant, _size, _state);

    /// <summary>
    /// Four terms of which two are null: the shape the ladder cascades through twice, and the one where
    /// a single pass over the terms should be furthest ahead.
    /// </summary>
    [Benchmark(Description = "site: generated class, four terms, two null")]
    [BenchmarkCategory(Site)]
    public string? Site_GeneratedFourWithNulls() =>
        ClassJoinCandidates.Generated(_card, _absent, _size, _absent);

    /// <summary>The same shape through the runtime method.</summary>
    [Benchmark(Description = "site: runtime method, four terms, two null")]
    [BenchmarkCategory(Site)]
    public string? Site_RuntimeFourWithNulls() =>
        ClassJoinCandidates.Runtime(_card, _absent, _size, _absent);
}

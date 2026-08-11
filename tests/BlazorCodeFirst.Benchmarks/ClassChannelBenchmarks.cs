using BenchmarkDotNet.Attributes;
using BlazorCodeFirst.Benchmarks.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.Benchmarks;

/// <summary>
/// What the class channel's join allocates per build, for #236. Run before and after a change to the
/// generation rule; the change is only worth taking if no shape allocates more than it did.
/// </summary>
/// <remarks>
/// Not a <c>DESIGN.md</c> §7.1 figure. §7.1 publishes a comparison against an equivalent Razor
/// component, gated by <c>Program.Main</c> on frame equivalence, and there is no Razor spelling of an
/// additive class channel to put on the other side. These numbers answer a narrower question — whether
/// a generation rule got more expensive than the one it replaces — and live in #236.
/// </remarks>
[MemoryDiagnoser]
public class ClassChannelBenchmarks
{
    private readonly ClassJoinNonNullView _nonNull = new();
    private readonly ClassJoinNullView _null = new();
    private readonly ClassJoinThreeView _three = new();
    private readonly RenderTreeBuilder _builder = new();

    [GlobalCleanup]
    public void Cleanup() => _builder.Dispose();

    /// <summary>Two terms, neither null. The row that decided #236.</summary>
    [Benchmark(Baseline = true, Description = "class join: two non-null terms")]
    public void Join_TwoNonNullTerms()
    {
        _builder.Clear();
        _nonNull.Build(_builder);
    }

    /// <summary>Two terms, the second null at render time.</summary>
    [Benchmark(Description = "class join: second term null")]
    public void Join_SecondTermNull()
    {
        _builder.Clear();
        _null.Build(_builder);
    }

    /// <summary>Three terms, none null.</summary>
    [Benchmark(Description = "class join: three non-null terms")]
    public void Join_ThreeNonNullTerms()
    {
        _builder.Clear();
        _three.Build(_builder);
    }
}

using BlazorCodeFirst.Compiler.Analysis;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Marks <see cref="KnownSymbolsCacheTests"/> as a collection xUnit runs outside the parallel phase, after
/// every parallelized collection has finished.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KnownSymbolsCacheSerialization
{
    public const string Name = "KnownSymbols cache";
}

/// <summary>
/// <see cref="KnownSymbols.TryCreate"/> memoizes its result per <see cref="Microsoft.CodeAnalysis.Compilation"/>
/// instance (#514), instead of rebuilding the ~20 <c>GetTypeByMetadataName</c> calls and three member walks on
/// every candidate component and view part.
/// </summary>
/// <remarks>
/// The cache these tests observe is process-wide static state, shared with every other test in this suite
/// that also calls <c>TryCreate</c>, the same way <c>CompilationTestHost</c>'s own reference-set caches
/// already are. A same-method pair of calls here has no await between them, so an xUnit-parallelized test
/// from another class only needs to complete <see cref="KnownSymbols.CacheCapacityForTests"/> distinct
/// <c>TryCreate</c> calls inside that gap to evict an entry these tests still expect. That gap is not
/// negligible: <c>TryCreate</c> builds the evicting entry itself while holding the cache's lock, so a
/// contended lock at suite start lets several such builds land inside one pair of calls. Observed locally —
/// <c>TryCreate_CalledTwiceWithSameCompilation_ReturnsTheSameInstance</c> failed on the third repetition of a
/// full <c>dotnet test</c> run under default parallelization. <see cref="KnownSymbolsCacheSerialization"/> is
/// what removes the race, not a low enough probability to ignore: it runs this class outside the parallel
/// phase, so no other class's <c>TryCreate</c> call can land inside these gaps.
/// </remarks>
[Collection(KnownSymbolsCacheSerialization.Name)]
public sealed class KnownSymbolsCacheTests
{
    [Fact]
    public void TryCreate_CalledTwiceWithSameCompilation_ReturnsTheSameInstance()
    {
        var compilation = CompilationTestHost.CreateCompilation("");

        var first = KnownSymbols.TryCreate(compilation);
        var second = KnownSymbols.TryCreate(compilation);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void TryCreate_CalledWithTwoDistinctCompilations_ReturnsDistinctInstances()
    {
        var first = KnownSymbols.TryCreate(CompilationTestHost.CreateCompilation(""));
        var second = KnownSymbols.TryCreate(CompilationTestHost.CreateCompilation(""));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void TryCreate_WithoutTheRuntimeReferenced_ReturnsNullOnEveryCall()
    {
        var compilation = CompilationTestHost.CreateCompilationWithoutRuntime(("Test.cs", ""));

        Assert.Null(KnownSymbols.TryCreate(compilation));
        Assert.Null(KnownSymbols.TryCreate(compilation));
    }

    /// <summary>
    /// The cache is bounded (#514's fix note): once more distinct compilations than its capacity have been
    /// resolved, the oldest entry is evicted and a later call for that same compilation rebuilds rather than
    /// returning a stale instance forever, so a long-running IDE session cannot pin an unbounded number of
    /// old <c>Compilation</c> instances alive.
    /// </summary>
    [Fact]
    public void TryCreate_AfterExceedingCacheCapacity_RebuildsTheEvictedCompilationsEntry()
    {
        var oldest = CompilationTestHost.CreateCompilation("");
        var oldestFirstResult = KnownSymbols.TryCreate(oldest);

        for (var i = 0; i < KnownSymbols.CacheCapacityForTests; i++)
            KnownSymbols.TryCreate(CompilationTestHost.CreateCompilation(""));

        var oldestSecondResult = KnownSymbols.TryCreate(oldest);

        Assert.NotNull(oldestFirstResult);
        Assert.NotNull(oldestSecondResult);
        Assert.NotSame(oldestFirstResult, oldestSecondResult);
    }
}

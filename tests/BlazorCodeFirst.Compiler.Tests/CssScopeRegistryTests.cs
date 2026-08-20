using BlazorCodeFirst.Compiler.Analysis;

namespace BlazorCodeFirst.Compiler.Tests;

public class CssScopeRegistryTests
{
    [Fact]
    public void TryGetScopeForComponentFile_ResolvesByAppendingDotCss()
    {
        var registry = CssScopeRegistry.Create(
            [new CssScopeEntry("/repo/App/Counter.cs.css", "bcf-abcd1234")]);

        var found = registry.TryGetScopeForComponentFile("/repo/App/Counter.cs", out var scope);

        Assert.True(found);
        Assert.Equal("bcf-abcd1234", scope);
    }

    [Fact]
    public void TryGetScopeForComponentFile_ReturnsFalseWhenNoMatchingCssFile()
    {
        var registry = CssScopeRegistry.Create(
            [new CssScopeEntry("/repo/App/Counter.cs.css", "bcf-abcd1234")]);

        var found = registry.TryGetScopeForComponentFile("/repo/App/NavMenu.cs", out var scope);

        Assert.False(found);
        Assert.Null(scope);
    }

    [Fact]
    public void TryGetScopeForComponentFile_IsCaseInsensitive()
    {
        // AdditionalFiles and Compile items are both discovered from the same on-disk glob, so their
        // casing already agrees in practice; case-insensitivity here is a zero-cost defense against a
        // build environment where the two happen to disagree (e.g. a manually authored Compile item),
        // never a behavior anything relies on.
        var registry = CssScopeRegistry.Create(
            [new CssScopeEntry("/repo/App/Counter.cs.css", "bcf-abcd1234")]);

        var found = registry.TryGetScopeForComponentFile("/REPO/App/Counter.cs", out var scope);

        Assert.True(found);
        Assert.Equal("bcf-abcd1234", scope);
    }

    [Fact]
    public void Create_WithNoEntries_ReturnsEmpty()
    {
        var registry = CssScopeRegistry.Create([]);

        Assert.Same(CssScopeRegistry.Empty, registry);
    }

    [Fact]
    public void Create_DeduplicatesByCssFilePathKeepingFirstOccurrence()
    {
        var registry = CssScopeRegistry.Create(
        [
            new CssScopeEntry("/repo/App/Counter.cs.css", "bcf-first"),
            new CssScopeEntry("/repo/App/Counter.cs.css", "bcf-second"),
        ]);

        registry.TryGetScopeForComponentFile("/repo/App/Counter.cs", out var scope);
        Assert.Equal("bcf-first", scope);
        Assert.Single(registry.Entries.AsImmutableArray());
    }

    [Fact]
    public void TwoRegistriesWithTheSameEntriesInDifferentOrder_AreEqual()
    {
        var left = CssScopeRegistry.Create(
        [
            new CssScopeEntry("/repo/App/Counter.cs.css", "bcf-1"),
            new CssScopeEntry("/repo/App/NavMenu.cs.css", "bcf-2"),
        ]);
        var right = CssScopeRegistry.Create(
        [
            new CssScopeEntry("/repo/App/NavMenu.cs.css", "bcf-2"),
            new CssScopeEntry("/repo/App/Counter.cs.css", "bcf-1"),
        ]);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}

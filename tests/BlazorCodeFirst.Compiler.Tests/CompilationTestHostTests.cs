namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Covers the test host's own contracts, where a defect makes every test built on it prove less than it
/// appears to.
/// </summary>
public sealed class CompilationTestHostTests
{
    [Fact]
    public void BuildMetadataReferences_WithoutRuntime_DoesNotReferenceTheRuntimeAssembly()
    {
        var displays = CompilationTestHost.BuildMetadataReferences(includeRuntime: false)
            .Select(static reference => reference.Display ?? string.Empty);

        Assert.DoesNotContain(
            displays,
            static display => display.EndsWith("BlazorCodeFirst.Runtime.dll", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildMetadataReferences_WithoutRuntime_StillReferencesBlazorComponents()
    {
        var displays = CompilationTestHost.BuildMetadataReferences(includeRuntime: false)
            .Select(static reference => reference.Display ?? string.Empty);

        Assert.Contains(
            displays,
            static display => display.EndsWith(
                "Microsoft.AspNetCore.Components.dll", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildMetadataReferences_WithRuntime_ReferencesTheRuntimeAssembly()
    {
        var displays = CompilationTestHost.BuildMetadataReferences()
            .Select(static reference => reference.Display ?? string.Empty);

        Assert.Contains(
            displays,
            static display => display.EndsWith("BlazorCodeFirst.Runtime.dll", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The set is built once and shared (#217), and nothing else in this suite would report a revert to
    /// per-call construction.
    /// </summary>
    [Fact]
    public void BuildMetadataReferences_RepeatedCalls_ReturnTheSameSet()
    {
        var first = CompilationTestHost.BuildMetadataReferences();
        var second = CompilationTestHost.BuildMetadataReferences();

        Assert.True(first == second, "Expected the cached reference set, not a freshly built one.");
    }

    /// <summary>
    /// Covers the <c>includeComponentsWeb</c> filter the way the tests above cover
    /// <c>includeRuntime</c>, and with it the cache key: a key that named only one of the two flags
    /// would hand this call the other combination's set, restoring by cache the very reference the
    /// filter exists to remove.
    /// </summary>
    [Fact]
    public void BuildMetadataReferences_WithoutComponentsWeb_DoesNotReferenceThatAssembly()
    {
        // Ask for the default combination first, so a key that ignored includeComponentsWeb would have
        // that set cached and hand it back below rather than losing a race to decide which one wins.
        _ = CompilationTestHost.BuildMetadataReferences();

        var displays = CompilationTestHost.BuildMetadataReferences(includeComponentsWeb: false)
            .Select(static reference => reference.Display ?? string.Empty);

        Assert.DoesNotContain(
            displays,
            static display => display.EndsWith(
                "Microsoft.AspNetCore.Components.Web.dll", System.StringComparison.OrdinalIgnoreCase));
    }
}

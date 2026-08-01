using System.Linq;

namespace BlazorCompose.Compiler.Tests;

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
            static display => display.EndsWith("BlazorCompose.Runtime.dll", System.StringComparison.OrdinalIgnoreCase));
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
            static display => display.EndsWith("BlazorCompose.Runtime.dll", System.StringComparison.OrdinalIgnoreCase));
    }
}

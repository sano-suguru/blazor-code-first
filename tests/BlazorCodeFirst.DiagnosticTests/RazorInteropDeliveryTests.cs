namespace BlazorCodeFirst.DiagnosticTests;

[Collection(RealBuildDiagnostics.Name)]
public sealed class RazorInteropDeliveryTests(RazorInteropFixtures fixtures)
{
    [Fact]
    public void ProjectReference_RazorComponent_ResolvesAndEmitsOpenComponent()
    {
        var build = fixtures.ProjectReference;

        Assert.Contains(
            "__builder.OpenComponent<global::RazorInteropFixture.ReferencedRazorComponent>(0);",
            build.GeneratedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BCF3012", build.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_RazorComponent_ResolvesFromIsolatedPackageAndEmitsOpenComponent()
    {
        var build = fixtures.Package;

        Assert.Contains(
            "__builder.OpenComponent<global::RazorInteropFixture.ReferencedRazorComponent>(0);",
            build.GeneratedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BCF3012", build.Output, StringComparison.Ordinal);

        var assets = File.ReadAllText(build.AssetsFilePath);
        Assert.Contains("blazorcodefirst.razorinteropfixture/0.1.0-dev", assets, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blazorcodefirst/0.1.0-dev", assets, StringComparison.OrdinalIgnoreCase);
    }
}

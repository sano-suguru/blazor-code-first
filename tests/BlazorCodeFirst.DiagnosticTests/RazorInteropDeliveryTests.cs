namespace BlazorCodeFirst.DiagnosticTests;

[Collection(RealBuildDiagnostics.Name)]
public sealed class RazorInteropDeliveryTests(RazorInteropFixtures fixtures)
{
    [Fact]
    public void ProjectReference_RazorComponent_ResolvesAndEmitsOpenComponent()
    {
        var build = fixtures.ProjectReference;

        Assert.True(build.ExitCode == 0, build.Output);
        Assert.Contains(
            "__builder.OpenComponent<global::RazorInteropFixture.ReferencedRazorComponent>(0);",
            build.GeneratedSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BCF3012", build.Output, StringComparison.Ordinal);
    }
}

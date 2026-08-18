namespace BlazorCodeFirst.DiagnosticTests;

public sealed class ScopedCssFixturesTests
{
    [Fact]
    public void ProjectReference_fixture_bundles_rewritten_scoped_css()
    {
        var build = ScopedCssFixtures.BuildProjectReference();

        Assert.Contains(".my-component[bcf-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("color: red;", build.BundledCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_fixture_bundles_rewritten_scoped_css()
    {
        var build = ScopedCssFixtures.BuildPackage();

        Assert.Contains(".my-component[bcf-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("color: red;", build.BundledCss, StringComparison.Ordinal);
    }
}

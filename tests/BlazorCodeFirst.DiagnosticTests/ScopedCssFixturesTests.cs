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

    [Fact]
    public void Mixed_fixture_bundle_contains_both_bcf_and_razor_scoped_css()
    {
        // Exercises the false branch of BcfBundleScopedCss's Condition: a project with a real
        // .razor.css file alongside a .cs.css file leaves @(_ScopedCss) non-empty, so the SDK's own
        // BundleScopedCssFiles writes the app bundle itself and BcfBundleScopedCss stays inert. The
        // ProjectReference/Package fixtures above only ever exercise the true branch (no .razor.css
        // at all), so this is the only fixture that proves the SDK-writes-the-bundle path still
        // includes BCF's contribution correctly.
        var build = ScopedCssFixtures.BuildMixed();

        Assert.Contains(".my-component[bcf-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("color: red;", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains(".my-widget[b-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("color: blue;", build.BundledCss, StringComparison.Ordinal);
    }
}

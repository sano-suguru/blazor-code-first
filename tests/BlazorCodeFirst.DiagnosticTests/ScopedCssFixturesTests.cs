namespace BlazorCodeFirst.DiagnosticTests;

[Collection(RealBuildDiagnostics.Name)]
public sealed class ScopedCssFixturesTests(ScopedCssFixtures fixtures)
{
    [Fact]
    public void ProjectReference_fixture_bundles_rewritten_scoped_css()
    {
        var build = fixtures.ProjectReference;

        Assert.Contains(".my-component[bcf-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("color: red;", build.BundledCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Package_fixture_bundles_rewritten_scoped_css()
    {
        var build = fixtures.Package;

        Assert.Contains(".my-component[bcf-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("color: red;", build.BundledCss, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectReference_fixture_generated_component_carries_the_bundles_own_scope_hash()
    {
        var build = fixtures.ProjectReference;

        var bundleScope = ExtractScope(build.BundledCss);

        var generatedFiles = Directory.GetFiles(
            build.GeneratedFilesDirectory, "*Counter.g.cs", SearchOption.AllDirectories);
        var generatedSource = File.ReadAllText(Assert.Single(generatedFiles));

        Assert.Contains($"__builder.AddAttribute(2, \"{bundleScope}\");", generatedSource);

        // Cross-check against a hash computed independently of the build, not just against the
        // bundle's own claim: proves the *generator's* path-matching (AdditionalText.Path vs
        // SyntaxTree.FilePath) landed on the value BlazorCodeFirst.Build actually assigned, rather
        // than both sides of this test agreeing by construction.
        var expectedScope = BlazorCodeFirst.Build.ScopeIdentifier.Compute(
            projectDirectory: Path.Combine(RepoLayout.Root, "tests", "msbuild-fixtures", "ScopedCss.ProjectReference"),
            cssFilePath: Path.Combine(
                RepoLayout.Root, "tests", "msbuild-fixtures", "ScopedCss.ProjectReference", "Counter.cs.css"),
            assemblyName: "ScopedCss.ProjectReference");
        Assert.Equal(expectedScope, bundleScope);
    }

    [Fact]
    public void ProjectReference_fixture_generated_component_scopes_the_folded_markup_too()
    {
        var build = fixtures.ProjectReference;
        var bundleScope = ExtractScope(build.BundledCss);

        var generatedFiles = Directory.GetFiles(
            build.GeneratedFilesDirectory, "*Counter.g.cs", SearchOption.AllDirectories);
        var generatedSource = File.ReadAllText(Assert.Single(generatedFiles));

        Assert.Contains($"<span {bundleScope}>hello</span>", generatedSource);
    }

    private static string ExtractScope(string bundledCss)
    {
        var match = System.Text.RegularExpressions.Regex.Match(bundledCss, @"\[(bcf-[0-9a-f]{8})\]");
        Assert.True(match.Success, $"No bcf- scope attribute found in bundle.{Environment.NewLine}{bundledCss}");
        return match.Groups[1].Value;
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
        var build = fixtures.Mixed;

        Assert.Contains(".my-component[bcf-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("color: red;", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains(".my-widget[b-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("color: blue;", build.BundledCss, StringComparison.Ordinal);
    }
}

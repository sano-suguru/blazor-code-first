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

    [Fact]
    public void AtRules_fixture_bundles_media_keyframes_animation_and_deep_combinator()
    {
        var build = fixtures.AtRules;

        Assert.Contains(".my-component[bcf-", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 600px)", build.BundledCss, StringComparison.Ordinal);
        Assert.Matches(@"@keyframes my-fade-bcf-[0-9a-f]{8}", build.BundledCss);
        Assert.Matches(@"animation:\s*my-fade-bcf-[0-9a-f]{8}", build.BundledCss);
        Assert.DoesNotContain("::deep", build.BundledCss, StringComparison.Ordinal);
    }

    [Fact]
    public void Orphan_fixture_fails_the_build_with_BCF3041()
    {
        var (exitCode, output) = fixtures.Orphan;

        Assert.NotEqual(0, exitCode);
        Assert.Contains("BCF3041", output, StringComparison.Ordinal);
        Assert.Contains("Orphan.cs.css", output, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryProjectReference_fixture_app_bundle_imports_the_librarys_project_bundle()
    {
        var build = fixtures.LibraryProjectReference;

        Assert.Contains("@import '_content/", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains(".bundle.scp.css';", build.BundledCss, StringComparison.Ordinal);

        // "obj/Debug/", not the bare "obj/" directory: BuildFixture below always builds in the
        // default (Debug) configuration, but ScopedCss.Library is a shared fixture project also
        // packed in Release configuration by BuildLibraryPackage's Pack() call for the
        // LibraryPackage fixture -- searching the whole "obj/" tree finds both configurations'
        // outputs and breaks Assert.Single (measured: this test run alongside LibraryPackage's).
        var libraryBundlePaths = Directory.GetFiles(
            Path.Combine(RepoLayout.Root, "tests", "msbuild-fixtures", "ScopedCss.Library", "obj", "Debug"),
            "*.bundle.scp.css",
            SearchOption.AllDirectories);
        var libraryBundle = Assert.Single(libraryBundlePaths);
        var libraryBundleContent = File.ReadAllText(libraryBundle);

        Assert.Contains(".my-component[bcf-", libraryBundleContent, StringComparison.Ordinal);
        Assert.Contains("color: red;", libraryBundleContent, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryPackage_fixture_app_bundle_imports_the_librarys_project_bundle()
    {
        var build = fixtures.LibraryPackage;

        Assert.Contains("@import '_content/", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains(".bundle.scp.css';", build.BundledCss, StringComparison.Ordinal);

        var packageContentPaths = Directory.GetFiles(
            Path.Combine(RepoLayout.ArtifactsDirectory, "scoped-css-library", "packages",
                "blazorcodefirst.scopedcsslibraryfixture", RepoLayout.PackageVersion, "staticwebassets"),
            "*.bundle.scp.css",
            SearchOption.TopDirectoryOnly);
        var packagedBundle = Assert.Single(packageContentPaths);
        var packagedBundleContent = File.ReadAllText(packagedBundle);

        Assert.Contains(".my-component[bcf-", packagedBundleContent, StringComparison.Ordinal);
        Assert.Contains("color: red;", packagedBundleContent, StringComparison.Ordinal);
    }

    [Fact]
    public void LibraryMixed_fixture_app_bundle_imports_the_mixed_librarys_project_bundle()
    {
        var build = fixtures.LibraryMixed;

        Assert.Contains("@import '_content/", build.BundledCss, StringComparison.Ordinal);
        Assert.Contains(".bundle.scp.css';", build.BundledCss, StringComparison.Ordinal);

        var libraryBundlePaths = Directory.GetFiles(
            Path.Combine(RepoLayout.Root, "tests", "msbuild-fixtures", "ScopedCss.MixedLibrary", "obj"),
            "*.bundle.scp.css",
            SearchOption.AllDirectories);
        var libraryBundle = Assert.Single(libraryBundlePaths);
        var libraryBundleContent = File.ReadAllText(libraryBundle);

        Assert.Contains(".my-component[bcf-", libraryBundleContent, StringComparison.Ordinal);
        Assert.Contains("color: red;", libraryBundleContent, StringComparison.Ordinal);
        Assert.Contains(".my-widget[b-", libraryBundleContent, StringComparison.Ordinal);
        Assert.Contains("color: blue;", libraryBundleContent, StringComparison.Ordinal);
    }
}

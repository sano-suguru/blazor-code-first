namespace BlazorCompose.DiagnosticTests;

/// <summary>
/// Asserts that diagnostics reach an author building an ordinary project — the layer that did not
/// exist when BC1001 spent its whole life unreportable behind a green test suite (#76, #80).
/// </summary>
[Collection(RealBuildDiagnostics.Name)]
public sealed class DiagnosticDeliveryTests(DiagnosticFixtures fixtures)
{
    [Theory]
    [MemberData(nameof(DiagnosticExpectations.Ids), MemberType = typeof(DiagnosticExpectations))]
    public void EveryDeclaredDiagnostic_IsReportedByARealBuild(string id)
    {
        var expected = DiagnosticExpectations.For(id);
        var build = fixtures.ForKind(expected.Fixture);

        var actual = SingleDiagnostic(build, id);

        Assert.Equal(expected.Severity, actual.Severity);
        AssertLocation(build, expected, actual);
    }

    /// <summary>The one diagnostic with this id, or a failure naming everything the build did report.</summary>
    private static BuildDiagnostic SingleDiagnostic(FixtureBuild build, string id)
    {
        var matches = build.WithId(id);

        Assert.True(
            matches.Length == 1,
            $"Expected exactly one {id}, found {matches.Length}.{Environment.NewLine}{build.Report()}");

        return matches[0];
    }

    private static void AssertLocation(FixtureBuild build, DiagnosticExpectation expected, BuildDiagnostic actual)
    {
        if (expected.Anchor is null)
        {
            Assert.False(
                actual.HasLocation,
                $"{expected.Id} now has a location ({actual}). If that is the fix for #77, replace the " +
                $"null anchor in DiagnosticExpectations with the text it should point at.");
            return;
        }

        Assert.True(
            actual.HasLocation,
            $"{expected.Id} reached the build with no usable location: {actual}.{Environment.NewLine}{build.Report()}");
        Assert.Equal(expected.FileName, actual.FileName);
        Assert.Equal(expected.Anchor, build.AnchorTextOf(actual));

        // The anchor text alone cannot tell two identical spellings on one line apart, and for a
        // duplicate-binding diagnostic pointing at the first one would be actively misleading.
        var line = build.SourceLineOf(actual);
        var expectedColumn = 1 + (expected.Occurrence == AnchorOccurrence.Last
            ? line.LastIndexOf(expected.Anchor, StringComparison.Ordinal)
            : line.IndexOf(expected.Anchor, StringComparison.Ordinal));

        Assert.Equal(expectedColumn, actual.Column);
    }

    [Fact]
    public void AnalyzerFixture_ReportsAnUnrelatedAnalyzerRule()
    {
        // The control. Without it, "BC3001 was reported" only proves BC3001 works in a compilation
        // where analyzers happen to run; with it, the fixture itself is verified to be analyzer-eligible.
        var build = fixtures.AnalyzerViaProjectReference;

        Assert.True(build.Contains("CA1050"), build.Report());
        Assert.False(build.Contains("CS0534"), "The analyzer fixture must stay free of declaration errors." +
            Environment.NewLine + build.Report());
    }

    [Fact]
    public void GeneratorFixture_ReportsNoAnalyzerDiagnostics_BecauseADeclarationErrorSuppressesThem()
    {
        // The behaviour the whole design follows from (#76, ARCHITECTURE.md 付録A.0): csc does not run
        // the analyzer driver on a compilation that has a declaration-level error. The identical
        // CA1050-violating type is compiled into both fixtures, is reported in the analyzer one, and
        // is silently gone here. If this test ever fails, Roslyn changed and A.0 can be revisited.
        var build = fixtures.GeneratorViaProjectReference;

        Assert.True(build.Contains("CS0534"), build.Report());
        Assert.False(
            build.Contains("CA1050"),
            "An analyzer diagnostic survived a declaration-level error; re-examine 付録A.0." +
            Environment.NewLine + build.Report());
    }

    [Fact]
    public void GeneratorReportedDiagnostics_SurviveADeclarationError()
    {
        // The other half of the same finding, and the reason BC1001 had to move: the generator driver
        // has no such gate, so its diagnostics are reported next to the CS0534 they explain.
        var build = fixtures.GeneratorViaProjectReference;

        var bc1001 = SingleDiagnostic(build, "BC1001");
        var cs0534 = build.WithId("CS0534");

        Assert.Contains(cs0534, error => error.FilePath == bc1001.FilePath && error.Line == bc1001.Line);
    }

    [Fact]
    public void PackageDelivery_ReportsGeneratorDiagnostics()
    {
        // eng/verify-package.sh asserts the analyzer is IN the package. This asserts it WORKS from
        // there: same source, same diagnostics, delivered as an external consumer receives it.
        var build = fixtures.GeneratorViaPackage;

        var bc1001 = SingleDiagnostic(build, "BC1001");
        Assert.Equal("Bc1001Bc1005.cs", bc1001.FileName);
        Assert.Equal("Bc1001NonPartial", build.AnchorTextOf(bc1001));

        Assert.True(build.Contains("BC1005"), build.Report());
    }

    [Fact]
    public void PackageDelivery_ReportsAnalyzerDiagnostics()
    {
        var build = fixtures.AnalyzerViaPackage;

        var bc3001 = SingleDiagnostic(build, "BC3001");
        Assert.Equal("Mutating.cs", bc3001.FileName);
        Assert.Equal("_count++", build.AnchorTextOf(bc3001));

        Assert.True(build.Contains("CA1050"), build.Report());
    }
}

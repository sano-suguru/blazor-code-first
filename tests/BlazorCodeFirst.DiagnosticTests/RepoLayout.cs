namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>Paths the fixture builds need, resolved from the test assembly's location.</summary>
internal static class RepoLayout
{
    /// <summary>The repository root, found by walking up to the directory holding the solution.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Scratch space for SARIF logs and the packed package; git-ignored via <c>artifacts/</c>.</summary>
    public static string ArtifactsDirectory { get; } = Path.Combine(Root, "artifacts", "diagnostic-tests");

    /// <summary>
    /// The version the delivery packages are built and restored at, read from the one place the
    /// repository declares it (#228). The interop assertions check that a restore resolved
    /// BlazorCodeFirst at this version from the isolated local feed rather than from anywhere else, so
    /// they need the value; reading it here keeps them from carrying a literal that a version bump
    /// would silently strand.
    /// </summary>
    public static string PackageVersion { get; } = ReadPackageVersion();

    /// <summary>
    /// The exact host that started this test run when MSBuild published it, so a nested build cannot
    /// resolve a different dotnet from PATH than the one running the suite.
    /// </summary>
    public static string DotnetHost { get; } =
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)
            ? host
            : "dotnet";

    private static string ReadPackageVersion()
    {
        var versionsPropsPath = Path.Combine(Root, "eng", "Versions.props");
        var version = System.Xml.Linq.XDocument
            .Load(versionsPropsPath)
            .Descendants("BlazorCodeFirstPackageVersion")
            .FirstOrDefault()
            ?.Value
            .Trim();

        if (string.IsNullOrEmpty(version))
        {
            throw new InvalidOperationException(
                $"Could not read BlazorCodeFirstPackageVersion from {versionsPropsPath}.");
        }

        return version;
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlazorCodeFirst.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate BlazorCodeFirst.slnx above {AppContext.BaseDirectory}.");
    }
}

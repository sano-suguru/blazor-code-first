namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>Paths the fixture builds need, resolved from the test assembly's location.</summary>
internal static class RepoLayout
{
    /// <summary>The repository root, found by walking up to the directory holding the solution.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Scratch space for SARIF logs and the packed package; git-ignored via <c>artifacts/</c>.</summary>
    public static string ArtifactsDirectory { get; } = Path.Combine(Root, "artifacts", "diagnostic-tests");

    /// <summary>
    /// The exact host that started this test run when MSBuild published it, so a nested build cannot
    /// resolve a different dotnet from PATH than the one running the suite.
    /// </summary>
    public static string DotnetHost { get; } =
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)
            ? host
            : "dotnet";

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

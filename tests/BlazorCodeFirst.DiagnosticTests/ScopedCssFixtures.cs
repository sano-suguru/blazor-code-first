namespace BlazorCodeFirst.DiagnosticTests;

public sealed record ScopedCssBuild(string Output, string BundledCss, string GeneratedFilesDirectory);

/// <summary>
/// Shared across every test in the <see cref="RealBuildDiagnostics"/> collection (see that
/// definition's own summary for why): each nested build is cached in a <see cref="Lazy{T}"/> so a
/// test suite exercising more than one of these fixtures pays for the shared task-assembly build
/// and each fixture project's own build exactly once, matching <see cref="RazorInteropFixtures"/>'s
/// shape for the same reason.
/// </summary>
public sealed class ScopedCssFixtures
{
    private readonly Lazy<bool> _stagedBuildTask;
    private readonly Lazy<ScopedCssBuild> _projectReference;
    private readonly Lazy<ScopedCssBuild> _package;
    private readonly Lazy<ScopedCssBuild> _mixed;

    public ScopedCssFixtures()
    {
        _stagedBuildTask = new(StageBuildTaskAssembly, LazyThreadSafetyMode.ExecutionAndPublication);
        _projectReference = new(
            () => BuildFixture("ScopedCss.ProjectReference"), LazyThreadSafetyMode.ExecutionAndPublication);
        _package = new(BuildPackage, LazyThreadSafetyMode.ExecutionAndPublication);
        _mixed = new(() => BuildFixture("ScopedCss.Mixed"), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ScopedCssBuild ProjectReference => _projectReference.Value;

    public ScopedCssBuild Package => _package.Value;

    public ScopedCssBuild Mixed => _mixed.Value;

    // BlazorCodeFirst.targets resolves its UsingTask entries from "tasks/{tfm}/" relative to
    // itself (the layout Task 6 packages into the NuGet payload) -- staging the just-built DLL
    // there is what makes a fixture's own Import of that file resolve real tasks. See Task 4's
    // DiscoverScopedCssTargetTests for why a competing UsingTask in the fixture project itself
    // would not work (MSBuild keeps the first registration for a task name, not the last).
    private static bool StageBuildTaskAssembly()
    {
        var buildTaskProjectPath = Path.Combine(
            RepoLayout.Root, "src", "BlazorCodeFirst.Build", "BlazorCodeFirst.Build.csproj");

        var (taskBuildExitCode, taskBuildOutput) = NestedDotnet.Run(
            ["build", buildTaskProjectPath, "--nologo", "-v:m"],
            RepoLayout.Root);
        Assert.True(
            taskBuildExitCode == 0,
            $"Building BlazorCodeFirst.Build failed.{Environment.NewLine}{taskBuildOutput}");

        var packagedTaskDirectory = Path.Combine(
            RepoLayout.Root, "src", "BlazorCodeFirst.Build", "tasks", "net10.0");
        Directory.CreateDirectory(packagedTaskDirectory);
        File.Copy(
            Path.Combine(RepoLayout.Root, "src", "BlazorCodeFirst.Build", "bin", "Debug", "net10.0", "BlazorCodeFirst.Build.dll"),
            Path.Combine(packagedTaskDirectory, "BlazorCodeFirst.Build.dll"),
            overwrite: true);

        return true;
    }

    // Shared by every fixture that imports BlazorCodeFirst.props/.targets directly (as opposed to
    // BuildPackage, which restores a packed nupkg instead): stage the task assembly once, rebuild
    // the fixture project, then find the single resulting bundle.
    private ScopedCssBuild BuildFixture(string fixtureName)
    {
        _ = _stagedBuildTask.Value;

        var projectDirectory = Path.Combine(
            RepoLayout.Root, "tests", "msbuild-fixtures", fixtureName);
        var projectPath = Path.Combine(projectDirectory, $"{fixtureName}.csproj");
        Assert.True(File.Exists(projectPath), $"Fixture project not found: {projectPath}");

        // Written outside the project directory, not to a "gen" subfolder under it: the SDK's default
        // Compile glob ("**/*.cs") would otherwise re-include the emitted files as ordinary source on
        // the next build, colliding with the same partial class the generator produces live (measured
        // -- this is exactly what happened when the output path was project-local).
        var generatedFilesDirectory = Path.Combine(
            RepoLayout.ArtifactsDirectory, "scoped-css", "gen", fixtureName);

        var (exitCode, output) = NestedDotnet.Run(
            ["build", projectPath, "-t:Rebuild", "--nologo", "-v:m",
             "-p:EmitCompilerGeneratedFiles=true",
             "-p:CompilerGeneratedFilesOutputPath=" + generatedFilesDirectory],
            projectDirectory);

        Assert.True(exitCode == 0, $"Building {fixtureName} failed.{Environment.NewLine}{output}");

        var bundlePath = Directory.GetFiles(
            Path.Combine(projectDirectory, "obj"), "*.styles.css", SearchOption.AllDirectories);
        var bundle = Assert.Single(bundlePath);

        return new ScopedCssBuild(output, File.ReadAllText(bundle), generatedFilesDirectory);
    }

    private static ScopedCssBuild BuildPackage()
    {
        var packageFeed = Path.Combine(RepoLayout.ArtifactsDirectory, "scoped-css", "feed");
        if (Directory.Exists(packageFeed))
            Directory.Delete(packageFeed, recursive: true);
        Directory.CreateDirectory(packageFeed);

        var (packExitCode, packOutput) = NestedDotnet.Run(
            ["pack", Path.Combine(RepoLayout.Root, "src", "BlazorCodeFirst.Runtime", "BlazorCodeFirst.Runtime.csproj"),
             "-c", "Release", "-o", packageFeed, "--nologo", "-v:m"],
            RepoLayout.Root);
        Assert.True(packExitCode == 0, $"Packing BlazorCodeFirst.Runtime failed.{Environment.NewLine}{packOutput}");

        var projectDirectory = Path.Combine(RepoLayout.Root, "tests", "msbuild-fixtures", "ScopedCss.Package");
        var projectPath = Path.Combine(projectDirectory, "ScopedCss.Package.csproj");
        var configFile = Path.Combine(projectDirectory, "NuGet.config");
        // Outside the project directory; see BuildFixture's comment on the same property.
        var generatedFilesDirectory = Path.Combine(
            RepoLayout.ArtifactsDirectory, "scoped-css", "gen", "ScopedCss.Package");

        var (exitCode, output) = NestedDotnet.Run(
            ["build", projectPath, "-t:Rebuild", "--nologo", "-v:m",
             "-p:RestoreConfigFile=" + configFile, "-p:RestoreForce=true",
             "-p:EmitCompilerGeneratedFiles=true",
             "-p:CompilerGeneratedFilesOutputPath=" + generatedFilesDirectory],
            projectDirectory);

        Assert.True(exitCode == 0, $"Building ScopedCss.Package failed.{Environment.NewLine}{output}");

        var bundlePaths = Directory.GetFiles(
            Path.Combine(projectDirectory, "obj"), "*.styles.css", SearchOption.AllDirectories);
        var packageBundle = Assert.Single(bundlePaths);

        return new ScopedCssBuild(output, File.ReadAllText(packageBundle), generatedFilesDirectory);
    }
}

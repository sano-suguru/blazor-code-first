namespace BlazorCodeFirst.DiagnosticTests;

public sealed record ScopedCssBuild(string Output, string BundledCss);

public sealed class ScopedCssFixtures
{
    // BlazorCodeFirst.targets resolves its UsingTask entries from "tasks/{tfm}/" relative to
    // itself (the layout Task 6 packages into the NuGet payload) -- staging the just-built DLL
    // there is what makes a fixture's own Import of that file resolve real tasks. See Task 4's
    // DiscoverScopedCssTargetTests for why a competing UsingTask in the fixture project itself
    // would not work (MSBuild keeps the first registration for a task name, not the last).
    private static void StageBuildTaskAssembly()
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
    }

    public static ScopedCssBuild BuildProjectReference()
    {
        StageBuildTaskAssembly();

        var projectDirectory = Path.Combine(
            RepoLayout.Root, "tests", "msbuild-fixtures", "ScopedCss.ProjectReference");
        var projectPath = Path.Combine(projectDirectory, "ScopedCss.ProjectReference.csproj");
        Assert.True(File.Exists(projectPath), $"Fixture project not found: {projectPath}");

        var (exitCode, output) = NestedDotnet.Run(
            ["build", projectPath, "-t:Rebuild", "--nologo", "-v:m"],
            projectDirectory);

        Assert.True(exitCode == 0, $"Building ScopedCss.ProjectReference failed.{Environment.NewLine}{output}");

        var bundlePath = Directory.GetFiles(
            Path.Combine(projectDirectory, "obj"), "*.styles.css", SearchOption.AllDirectories);
        var bundle = Assert.Single(bundlePath);

        return new ScopedCssBuild(output, File.ReadAllText(bundle));
    }

    public static ScopedCssBuild BuildPackage()
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

        var (exitCode, output) = NestedDotnet.Run(
            ["build", projectPath, "-t:Rebuild", "--nologo", "-v:m",
             "-p:RestoreConfigFile=" + configFile, "-p:RestoreForce=true"],
            projectDirectory);

        Assert.True(exitCode == 0, $"Building ScopedCss.Package failed.{Environment.NewLine}{output}");

        var bundlePaths = Directory.GetFiles(
            Path.Combine(projectDirectory, "obj"), "*.styles.css", SearchOption.AllDirectories);
        var packageBundle = Assert.Single(bundlePaths);

        return new ScopedCssBuild(output, File.ReadAllText(packageBundle));
    }

    public static ScopedCssBuild BuildMixed()
    {
        StageBuildTaskAssembly();

        var projectDirectory = Path.Combine(
            RepoLayout.Root, "tests", "msbuild-fixtures", "ScopedCss.Mixed");
        var projectPath = Path.Combine(projectDirectory, "ScopedCss.Mixed.csproj");
        Assert.True(File.Exists(projectPath), $"Fixture project not found: {projectPath}");

        var (exitCode, output) = NestedDotnet.Run(
            ["build", projectPath, "-t:Rebuild", "--nologo", "-v:m"],
            projectDirectory);

        Assert.True(exitCode == 0, $"Building ScopedCss.Mixed failed.{Environment.NewLine}{output}");

        var bundlePath = Directory.GetFiles(
            Path.Combine(projectDirectory, "obj"), "*.styles.css", SearchOption.AllDirectories);
        var bundle = Assert.Single(bundlePath);

        return new ScopedCssBuild(output, File.ReadAllText(bundle));
    }
}

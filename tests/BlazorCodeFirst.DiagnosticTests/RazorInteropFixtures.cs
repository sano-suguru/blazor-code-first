namespace BlazorCodeFirst.DiagnosticTests;

public sealed record RazorInteropBuild(
    int ExitCode,
    string Output,
    string GeneratedSource,
    string AssetsFilePath);

public sealed class RazorInteropFixtures
{
    private readonly Lazy<string> _packageFeed;
    private readonly Lazy<RazorInteropBuild> _package;
    private readonly Lazy<RazorInteropBuild> _projectReference;

    public RazorInteropFixtures()
    {
        _packageFeed = new(PackPackageFeed, LazyThreadSafetyMode.ExecutionAndPublication);
        _package = new(BuildPackage, LazyThreadSafetyMode.ExecutionAndPublication);
        _projectReference = new(BuildProjectReference, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public RazorInteropBuild Package => _package.Value;

    public RazorInteropBuild ProjectReference => _projectReference.Value;

    private static RazorInteropBuild BuildProjectReference() =>
        BuildConsumer("RazorInterop.ProjectReference", restoreConfigFile: null);

    private RazorInteropBuild BuildPackage() =>
        BuildConsumer(
            "RazorInterop.Package",
            Path.Combine(RepoLayout.Root, "tests", "msbuild-fixtures", "RazorInterop.Package", "NuGet.config"),
            restoreForce: true,
            _packageFeed.Value);

    private static RazorInteropBuild BuildConsumer(
        string consumer,
        string? restoreConfigFile,
        bool restoreForce = false,
        string? packageFeed = null)
    {
        var projectDirectory = Path.Combine(RepoLayout.Root, "tests", "msbuild-fixtures", consumer);
        var projectPath = Path.Combine(projectDirectory, consumer + ".csproj");
        Assert.True(File.Exists(projectPath), $"Fixture project not found: {projectPath}");

        var projectXml = File.ReadAllText(projectPath);
        if (packageFeed is not null)
            Assert.DoesNotContain("<ProjectReference", projectXml, StringComparison.Ordinal);

        var generatedOutputDirectory = Path.Combine(RepoLayout.ArtifactsDirectory, "razor-interop", consumer);
        if (Directory.Exists(generatedOutputDirectory))
            Directory.Delete(generatedOutputDirectory, recursive: true);

        Directory.CreateDirectory(generatedOutputDirectory);

        var arguments = new List<string>
        {
            "build",
            projectPath,
            "-t:Rebuild",
            "--nologo",
            "-v:m",
            "-p:EmitCompilerGeneratedFiles=true",
            "-p:CompilerGeneratedFilesOutputPath=" + generatedOutputDirectory,
        };

        if (restoreConfigFile is not null)
            arguments.Add("-p:RestoreConfigFile=" + restoreConfigFile);
        if (restoreForce)
            arguments.Add("-p:RestoreForce=true");

        var (exitCode, output) = NestedDotnet.Run(arguments, projectDirectory);
        var generatedFiles = Directory.GetFiles(generatedOutputDirectory, "*Host*.g.cs", SearchOption.AllDirectories);
        var generatedFile = Assert.Single(generatedFiles);
        Assert.StartsWith(
            Path.GetFullPath(generatedOutputDirectory) + Path.DirectorySeparatorChar,
            Path.GetFullPath(generatedFile),
            StringComparison.Ordinal);
        var generatedSource = File.ReadAllText(generatedFile);
        Assert.Contains("RenderView", generatedSource, StringComparison.Ordinal);

        var assetsFilePath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (packageFeed is not null)
        {
            var assets = File.ReadAllText(assetsFilePath);
            Assert.Contains("BlazorCodeFirst/0.1.0-dev", assets, StringComparison.Ordinal);
            Assert.Contains("BlazorCodeFirst.RazorInteropFixture/0.1.0-dev", assets, StringComparison.Ordinal);
        }

        return new RazorInteropBuild(
            exitCode,
            output,
            generatedSource,
            assetsFilePath);
    }

    private static string PackPackageFeed()
    {
        var packageFeed = Path.Combine(RepoLayout.ArtifactsDirectory, "razor-interop", "feed");
        if (Directory.Exists(packageFeed))
            Directory.Delete(packageFeed, recursive: true);

        Directory.CreateDirectory(packageFeed);

        Pack(
            Path.Combine(RepoLayout.Root, "src", "BlazorCodeFirst.Runtime", "BlazorCodeFirst.Runtime.csproj"),
            packageFeed);
        Pack(
            Path.Combine(RepoLayout.Root, "tests", "msbuild-fixtures", "RazorInterop.Library", "RazorInterop.Library.csproj"),
            packageFeed);

        return packageFeed;
    }

    private static void Pack(string projectPath, string packageFeed)
    {
        var (exitCode, output) = NestedDotnet.Run(
            ["pack", projectPath, "-c", "Release", "-o", packageFeed, "--nologo", "-v:m"],
            RepoLayout.Root);

        Assert.True(exitCode == 0, $"Packing '{projectPath}' failed.{Environment.NewLine}{output}");
    }
}

namespace BlazorCodeFirst.DiagnosticTests;

public sealed record RazorInteropBuild(
    int ExitCode,
    string Output,
    string GeneratedSource,
    string AssetsFilePath);

public sealed class RazorInteropFixtures
{
    private readonly Lazy<RazorInteropBuild> _projectReference;

    public RazorInteropFixtures() =>
        _projectReference = new(BuildProjectReference, LazyThreadSafetyMode.ExecutionAndPublication);

    public RazorInteropBuild ProjectReference => _projectReference.Value;

    private static RazorInteropBuild BuildProjectReference() =>
        BuildConsumer("RazorInterop.ProjectReference", restoreConfigFile: null);

    private static RazorInteropBuild BuildConsumer(string consumer, string? restoreConfigFile)
    {
        var projectDirectory = Path.Combine(RepoLayout.Root, "tests", "msbuild-fixtures", consumer);
        var projectPath = Path.Combine(projectDirectory, consumer + ".csproj");
        Assert.True(File.Exists(projectPath), $"Fixture project not found: {projectPath}");

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

        var (exitCode, output) = NestedDotnet.Run(arguments, projectDirectory);
        var generatedFiles = Directory.GetFiles(generatedOutputDirectory, "*Host*.g.cs", SearchOption.AllDirectories);
        var generatedFile = Assert.Single(generatedFiles);
        var generatedSource = File.ReadAllText(generatedFile);
        Assert.Contains("RenderView", generatedSource, StringComparison.Ordinal);

        return new RazorInteropBuild(
            exitCode,
            output,
            generatedSource,
            Path.Combine(projectDirectory, "obj", "project.assets.json"));
    }
}

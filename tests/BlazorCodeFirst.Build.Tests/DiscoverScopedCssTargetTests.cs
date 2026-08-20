using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public sealed class DiscoverScopedCssTargetTests : IDisposable
{
    private readonly string _projectDirectory;

    public DiscoverScopedCssTargetTests()
    {
        _projectDirectory = Directory.CreateTempSubdirectory("bcf-discover-scoped-css-").FullName;
    }

    public void Dispose() => Directory.Delete(_projectDirectory, recursive: true);

    [Fact]
    public void BcfDiscoverScopedCss_stamps_CssScope_on_AdditionalFiles_for_each_cs_css_file()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "Counter.cs.css"),
            ".my-component {\n    color: red;\n}\n");

        var repoRoot = FindRepoRoot();
        var buildTaskAssembly = Path.Combine(
            repoRoot, "src", "BlazorCodeFirst.Build", "bin", "Debug", "net10.0", "BlazorCodeFirst.Build.dll");
        Assert.True(
            File.Exists(buildTaskAssembly),
            $"Expected the task assembly to already be built at {buildTaskAssembly}. " +
            "Run 'dotnet build src/BlazorCodeFirst.Build/BlazorCodeFirst.Build.csproj' first.");

        // BlazorCodeFirst.targets resolves its own UsingTask entries from a "tasks/{tfm}/" directory
        // relative to itself (the layout Task 6 packages into the NuGet payload). Declaring a second,
        // conflicting UsingTask for the same task name in this generated project does not override
        // that one -- MSBuild resolves to whichever UsingTask registration for a given task name it
        // encounters FIRST while evaluating the project, and the imported .targets file's own
        // UsingTask entries are evaluated before anything declared later in this generated project.
        // So instead of fighting that resolution order, this test stages the already-built task
        // assembly at the exact relative path BlazorCodeFirst.targets already expects -- which also
        // means this test exercises the real packaged-layout resolution path, not a test-only stand-in.
        var packagedTaskDirectory = Path.Combine(repoRoot, "src", "BlazorCodeFirst.Build", "tasks", "net10.0");
        Directory.CreateDirectory(packagedTaskDirectory);
        File.Copy(
            buildTaskAssembly,
            Path.Combine(packagedTaskDirectory, "BlazorCodeFirst.Build.dll"),
            overwrite: true);

        var propsPath = Path.Combine(repoRoot, "src", "BlazorCodeFirst.Build", "build", "BlazorCodeFirst.props");
        var targetsPath = Path.Combine(repoRoot, "src", "BlazorCodeFirst.Build", "build", "BlazorCodeFirst.targets");

        // Microsoft.NET.Sdk.Razor, not the plain SDK: BcfDiscoverScopedCss now also runs
        // BcfRegisterScopedCssStaticWebAssets (Task 5), which calls the StaticWebAssets SDK's own
        // DefineStaticWebAssets task. That task is only registered under an SDK that imports
        // Microsoft.NET.Sdk.StaticWebAssets -- true of every real BCF consumer (Blazor projects all
        // pull it in transitively), so this fixture now matches that baseline instead of the plain
        // SDK it started with.
        var projectXml = $"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>App</AssemblyName>
              </PropertyGroup>
              <Import Project="{propsPath}" />
              <Import Project="{targetsPath}" />
            </Project>
            """;

        var projectPath = Path.Combine(_projectDirectory, "App.csproj");
        File.WriteAllText(projectPath, projectXml);

        var (exitCode, output) = RunDotnet(
            ["build", projectPath, "-t:BcfDiscoverScopedCss", "-getItem:AdditionalFiles", "--nologo", "-v:q"],
            _projectDirectory);

        Assert.True(exitCode == 0, $"dotnet build failed with exit code {exitCode}.{Environment.NewLine}{output}");

        using var document = JsonDocument.Parse(output);
        var additionalFiles = document.RootElement.GetProperty("Items").GetProperty("AdditionalFiles");

        JsonElement? counterCssItem = null;
        foreach (var item in additionalFiles.EnumerateArray())
        {
            if (item.GetProperty("Identity").GetString()!.EndsWith("Counter.cs.css", StringComparison.Ordinal))
                counterCssItem = item;
        }

        Assert.True(counterCssItem.HasValue, $"No AdditionalFiles item for Counter.cs.css.{Environment.NewLine}{output}");
        var cssScope = counterCssItem!.Value.GetProperty("CssScope").GetString();
        Assert.Matches("^bcf-[0-9a-f]{8}$", cssScope);
    }

    private static (int ExitCode, string Output) RunDotnet(string[] arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet.");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    private static string FindRepoRoot()
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

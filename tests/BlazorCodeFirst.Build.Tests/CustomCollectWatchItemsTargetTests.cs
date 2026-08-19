using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BlazorCodeFirst.Build.Tests;

public sealed class CustomCollectWatchItemsTargetTests : IDisposable
{
    private readonly string _projectDirectory;

    public CustomCollectWatchItemsTargetTests()
    {
        _projectDirectory = Directory.CreateTempSubdirectory("bcf-collect-watch-items-").FullName;
    }

    public void Dispose() => Directory.Delete(_projectDirectory, recursive: true);

    [Fact]
    public void BcfCustomCollectWatchItems_adds_cs_css_files_to_Watch()
    {
        File.WriteAllText(
            Path.Combine(_projectDirectory, "Counter.cs.css"),
            ".my-component {\n    color: red;\n}\n");

        var repoRoot = FindRepoRoot();
        var propsPath = Path.Combine(repoRoot, "src", "BlazorCodeFirst.Build", "build", "BlazorCodeFirst.props");
        var targetsPath = Path.Combine(repoRoot, "src", "BlazorCodeFirst.Build", "build", "BlazorCodeFirst.targets");

        var projectXml = $"""
            <Project Sdk="Microsoft.NET.Sdk">
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
            [
                "build", projectPath, "-t:_BcfCustomCollectWatchItems",
                "-getItem:Watch", "-getProperty:CustomCollectWatchItems", "--nologo", "-v:q",
            ],
            _projectDirectory);

        Assert.True(exitCode == 0, $"dotnet build failed with exit code {exitCode}.{Environment.NewLine}{output}");

        using var document = JsonDocument.Parse(output);
        var watchItems = document.RootElement.GetProperty("Items").GetProperty("Watch");

        var foundCounterCss = false;
        foreach (var item in watchItems.EnumerateArray())
        {
            if (item.GetProperty("Identity").GetString()!.EndsWith("Counter.cs.css", StringComparison.Ordinal))
                foundCounterCss = true;
        }

        Assert.True(foundCounterCss, $"No Watch item for Counter.cs.css.{Environment.NewLine}{output}");

        // Pins the other half of the wiring: dotnet watch never invokes this target by name (the
        // -t: call above only proves the target body works). It reaches it exclusively through
        // $(CustomCollectWatchItems), which DotNetWatch.targets folds into _CollectWatchItems's
        // DependsOnTargets. A typo in BlazorCodeFirst.props's property line would leave the -t: call
        // above green while dotnet watch itself never saw a .cs.css change.
        var customCollectWatchItems = document.RootElement.GetProperty("Properties")
            .GetProperty("CustomCollectWatchItems").GetString();
        Assert.Contains("_BcfCustomCollectWatchItems", customCollectWatchItems, StringComparison.Ordinal);
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

using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BlazorCodeFirst.DiagnosticTests;

internal static class NestedDotnet
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    public static (int ExitCode, string Output) Run(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(RepoLayout.DotnetHost)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        // MSBuild exports its own toolset paths to child processes. Inheriting them from the MSBuild
        // run that started this test would point the nested build at the wrong SDK.
        foreach (var variable in (string[])
            [
                "MSBuildSDKsPath",
                "MSBuildExtensionsPath",
                "MSBuildExtensionsPath32",
                "MSBuildExtensionsPath64",
                "MSBuildLoadMicrosoftTargetsReadOnly",
                "MSBuildStartupDirectory",
                "MSBUILD_EXE_PATH",
            ])
        {
            startInfo.Environment.Remove(variable);
        }

        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        // Console output only ever appears inside assertion failures; keep it readable everywhere.
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet.");

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => AppendLine(output, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(output, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)BuildTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"dotnet {string.Join(' ', arguments)} did not finish within {BuildTimeout}."));
        }

        // Flushes the asynchronous readers; the overload above returns before they are drained.
        process.WaitForExit();

        return (process.ExitCode, output.ToString());
    }

    // Shared by every fixture class that packs a project into a local feed (ScopedCssFixtures,
    // RazorInteropFixtures): same "pack -c Release -o <feed>" invocation shape, same failure
    // message, so it lives here once instead of once per fixture class.
    public static void Pack(string projectPath, string packageFeed)
    {
        var (exitCode, output) = Run(
            ["pack", projectPath, "-c", "Release", "-o", packageFeed, "--nologo", "-v:m"],
            RepoLayout.Root);

        Assert.True(exitCode == 0, $"Packing '{projectPath}' failed.{Environment.NewLine}{output}");
    }

    private static void AppendLine(StringBuilder builder, string? line)
    {
        if (line is null)
            return;

        lock (builder)
            builder.AppendLine(line);
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>
/// Builds the projects under <c>tests/diagnostic-fixtures</c> with real MSBuild and exposes what the
/// compiler reported.  Shared across the whole test collection: each fixture is built at most once
/// per test run, on first use.
/// </summary>
/// <remarks>
/// This is the layer issue #80 exists to add.  Every other diagnostic test in the repository drives
/// the generator or an analyzer in-process, which cannot observe whether MSBuild delivers the
/// analyzer at all, whether csc runs it, or where the diagnostic lands in the author's file.
/// </remarks>
public sealed class DiagnosticFixtures
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Lazy<FixtureBuild>> _builds = new(StringComparer.Ordinal);
    private readonly Lazy<string> _packageDirectory;

    public DiagnosticFixtures() =>
        _packageDirectory = new Lazy<string>(PackRuntime, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Analyzer-reported diagnostics, analyzer delivered by ProjectReference.</summary>
    public FixtureBuild AnalyzerViaProjectReference => Build("AnalyzerDelivery.ProjectReference");

    /// <summary>Generator-reported diagnostics, analyzer delivered by ProjectReference.</summary>
    public FixtureBuild GeneratorViaProjectReference => Build("GeneratorDelivery.ProjectReference");

    /// <summary>Analyzer-reported diagnostics, analyzer delivered inside the NuGet package.</summary>
    public FixtureBuild AnalyzerViaPackage => Build("AnalyzerDelivery.Package");

    /// <summary>Generator-reported diagnostics, analyzer delivered inside the NuGet package.</summary>
    public FixtureBuild GeneratorViaPackage => Build("GeneratorDelivery.Package");

    public FixtureBuild ForKind(FixtureKind kind) => kind switch
    {
        FixtureKind.AnalyzerViaProjectReference => AnalyzerViaProjectReference,
        FixtureKind.GeneratorViaProjectReference => GeneratorViaProjectReference,
        FixtureKind.AnalyzerViaPackage => AnalyzerViaPackage,
        FixtureKind.GeneratorViaPackage => GeneratorViaPackage,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown fixture."),
    };

    private FixtureBuild Build(string name) =>
        _builds.GetOrAdd(
            name,
            static (key, self) => new Lazy<FixtureBuild>(
                () => self.BuildCore(key),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this).Value;

    private FixtureBuild BuildCore(string name)
    {
        var projectDirectory = Path.Combine(RepoLayout.Root, "tests", "diagnostic-fixtures", name);
        var projectPath = Path.Combine(projectDirectory, name + ".csproj");
        Assert.True(File.Exists(projectPath), $"Fixture project not found: {projectPath}");

        // Deleted first so a stale log from an earlier run can never be mistaken for this build's
        // output. Every fixture fails to compile, so csc produces no assembly and is never skipped as
        // up-to-date — an incremental build re-runs it and rewrites the log.
        var sarifPath = Path.Combine(RepoLayout.ArtifactsDirectory, name + ".sarif");
        Directory.CreateDirectory(RepoLayout.ArtifactsDirectory);
        File.Delete(sarifPath);

        var usesPackage = name.EndsWith(".Package", StringComparison.Ordinal);

        var arguments = new List<string>
        {
            "build",
            projectPath,
            "--nologo",
            "-v:m",
            "-p:DiagnosticSarifPath=" + sarifPath,
        };

        if (usesPackage)
        {
            // The package under test is the one packed for this run, and restore must actually re-run:
            // the fixture purges the extracted 0.1.0-dev folder, and an up-to-date assets file would
            // otherwise leave nothing to compile against.
            _ = _packageDirectory.Value;
            arguments.Add("-p:RestoreConfigFile=" + Path.Combine(projectDirectory, "NuGet.config"));
            arguments.Add("-p:RestoreForce=true");
        }

        var (_, output) = RunDotnet(arguments, projectDirectory);

        // The exit code is deliberately ignored: every fixture is expected to fail to build. A missing
        // log means the failure happened before csc ran (restore, a bad project file), which the raw
        // output explains far better than any assertion on the diagnostics would.
        Assert.True(
            File.Exists(sarifPath),
            $"Fixture '{name}' produced no SARIF log at {sarifPath}; the build failed before csc ran." +
            Environment.NewLine + output);

        return new FixtureBuild(name, projectDirectory, SarifLog.Read(sarifPath), output);
    }

    /// <summary>
    /// Packs the runtime into a directory used by nothing else, so the package fixtures always consume
    /// the analyzer as just built rather than whatever <c>artifacts/package</c> happens to hold from an
    /// earlier local run.
    /// </summary>
    private static string PackRuntime()
    {
        var packageDirectory = Path.Combine(RepoLayout.ArtifactsDirectory, "package");
        if (Directory.Exists(packageDirectory))
            Directory.Delete(packageDirectory, recursive: true);

        Directory.CreateDirectory(packageDirectory);

        var (exitCode, output) = RunDotnet(
            [
                "pack",
                Path.Combine(RepoLayout.Root, "src", "BlazorCodeFirst.Runtime", "BlazorCodeFirst.Runtime.csproj"),
                "-c",
                "Release",
                "-o",
                packageDirectory,
                "--nologo",
                "-v:m",
            ],
            RepoLayout.Root);

        Assert.True(exitCode == 0, "Packing BlazorCodeFirst.Runtime failed." + Environment.NewLine + output);

        return packageDirectory;
    }

    private static (int ExitCode, string Output) RunDotnet(IReadOnlyList<string> arguments, string workingDirectory)
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

    private static void AppendLine(StringBuilder builder, string? line)
    {
        if (line is null)
            return;

        lock (builder)
            builder.AppendLine(line);
    }
}

/// <summary>
/// One xunit collection for every test here: the fixtures share build output directories and the
/// packed package, so their builds must not overlap.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RealBuildDiagnostics : ICollectionFixture<DiagnosticFixtures>
{
    public const string Name = "real-build diagnostics";
}

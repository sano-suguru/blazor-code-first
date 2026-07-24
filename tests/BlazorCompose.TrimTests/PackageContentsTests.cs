using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace BlazorCompose.TrimTests;

public sealed class PackageContentsTests
{
    private const string PackageId = "BlazorCompose";
    private const string PackageVersion = "0.1.0-dev";
    private const string PackageReadmePath = "README.md";

    private static readonly string[] ExpectedPayloadFiles =
    [
        "analyzers/dotnet/cs/BlazorCompose.Compiler.dll",
        "lib/net10.0/BlazorCompose.Runtime.dll",
        "lib/net10.0/BlazorCompose.Runtime.xml"
    ];

    [Fact]
    public void Package_WhenPacked_ContainsOnlyExpectedPayloadAndMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageOutputDirectory = Path.Combine(repositoryRoot, "artifacts", "package-tests", nameof(Package_WhenPacked_ContainsOnlyExpectedPayloadAndMetadata));
        var packagePath = Path.Combine(packageOutputDirectory, $"{PackageId}.{PackageVersion}.nupkg");
        var runtimeProjectPath = Path.Combine(repositoryRoot, "src", "BlazorCompose.Runtime", "BlazorCompose.Runtime.csproj");
        var verificationScriptPath = Path.Combine(repositoryRoot, "eng", "verify-package.sh");

        if (Directory.Exists(packageOutputDirectory))
        {
            Directory.Delete(packageOutputDirectory, recursive: true);
        }

        Directory.CreateDirectory(packageOutputDirectory);

        RunDotNetPack(runtimeProjectPath, packageOutputDirectory, repositoryRoot);
        RunBash(verificationScriptPath, packagePath, repositoryRoot);

        using var packageStream = File.OpenRead(packagePath);
        using var packageArchive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        AssertPackageContents(packageArchive);
    }

    [Fact]
    public void RepeatedPack_WhenStagedCompilerOutputIsStale_RebuildsCompilerAnalyzer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageOutputRoot = Path.Combine(repositoryRoot, "artifacts", "package-tests", nameof(RepeatedPack_WhenStagedCompilerOutputIsStale_RebuildsCompilerAnalyzer));
        var firstPackageOutputDirectory = Path.Combine(packageOutputRoot, "first");
        var secondPackageOutputDirectory = Path.Combine(packageOutputRoot, "second");
        var secondPackagePath = Path.Combine(secondPackageOutputDirectory, $"{PackageId}.{PackageVersion}.nupkg");
        var runtimeProjectPath = Path.Combine(repositoryRoot, "src", "BlazorCompose.Runtime", "BlazorCompose.Runtime.csproj");
        var verificationScriptPath = Path.Combine(repositoryRoot, "eng", "verify-package.sh");
        var compilerOutputPath = GetPackCompilerAssemblyPath(repositoryRoot);

        if (Directory.Exists(packageOutputRoot))
        {
            Directory.Delete(packageOutputRoot, recursive: true);
        }

        Directory.CreateDirectory(firstPackageOutputDirectory);
        Directory.CreateDirectory(secondPackageOutputDirectory);

        RunDotNetPack(runtimeProjectPath, firstPackageOutputDirectory, repositoryRoot);

        Directory.CreateDirectory(Path.GetDirectoryName(compilerOutputPath)!);
        var staleCompilerBytes = "stale-compiler-output"u8.ToArray();

        try
        {
            File.WriteAllBytes(compilerOutputPath, staleCompilerBytes);

            RunDotNetPack(runtimeProjectPath, secondPackageOutputDirectory, repositoryRoot);
        }
        finally
        {
            if (File.Exists(compilerOutputPath))
            {
                File.Delete(compilerOutputPath);
            }
        }

        RunBash(verificationScriptPath, secondPackagePath, repositoryRoot);

        using var packageStream = File.OpenRead(secondPackagePath);
        using var packageArchive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        AssertPackageContents(packageArchive);

        var compilerEntry = packageArchive.GetEntry("analyzers/dotnet/cs/BlazorCompose.Compiler.dll");

        Assert.NotNull(compilerEntry);

        var packagedCompilerBytes = ReadAllBytes(compilerEntry);

        Assert.Equal((byte)'M', packagedCompilerBytes[0]);
        Assert.Equal((byte)'Z', packagedCompilerBytes[1]);
        Assert.False(packagedCompilerBytes.AsSpan().SequenceEqual(staleCompilerBytes));
    }

    [Fact]
    public void PackWithoutBuild_WhenStagedCompilerAnalyzerIsMissing_RegeneratesCompilerAnalyzer()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageOutputDirectory = Path.Combine(repositoryRoot, "artifacts", "package-tests", nameof(PackWithoutBuild_WhenStagedCompilerAnalyzerIsMissing_RegeneratesCompilerAnalyzer));
        var packagePath = Path.Combine(packageOutputDirectory, $"{PackageId}.{PackageVersion}.nupkg");
        var runtimeProjectPath = Path.Combine(repositoryRoot, "src", "BlazorCompose.Runtime", "BlazorCompose.Runtime.csproj");
        var verificationScriptPath = Path.Combine(repositoryRoot, "eng", "verify-package.sh");
        var compilerOutputPath = GetPackCompilerAssemblyPath(repositoryRoot);

        if (Directory.Exists(packageOutputDirectory))
        {
            Directory.Delete(packageOutputDirectory, recursive: true);
        }

        Directory.CreateDirectory(packageOutputDirectory);

        RunDotNetBuild(runtimeProjectPath, configuration: "Release", repositoryRoot);

        if (File.Exists(compilerOutputPath))
        {
            File.Delete(compilerOutputPath);
        }

        RunDotNetPack(runtimeProjectPath, packageOutputDirectory, repositoryRoot, noBuild: true);
        RunBash(verificationScriptPath, packagePath, repositoryRoot);

        using var packageStream = File.OpenRead(packagePath);
        using var packageArchive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        AssertPackageContents(packageArchive);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BlazorCompose.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string GetPackCompilerAssemblyPath(string repositoryRoot)
        => Path.Combine(
            repositoryRoot,
            "src",
            "BlazorCompose.Runtime",
            "obj",
            "compiler-pack",
            "analyzers",
            "dotnet",
            "cs",
            "BlazorCompose.Compiler.dll");

    private static void RunDotNetPack(string projectPath, string packageOutputDirectory, string workingDirectory)
        => RunDotNetPack(projectPath, packageOutputDirectory, workingDirectory, noBuild: false);

    private static void RunDotNetPack(string projectPath, string packageOutputDirectory, string workingDirectory, bool noBuild)
    {
        RunProcess(
            fileName: "dotnet",
            arguments: $"pack \"{projectPath}\" -c Release{(noBuild ? " --no-build" : string.Empty)} -o \"{packageOutputDirectory}\"",
            workingDirectory);
    }

    private static void RunDotNetBuild(string projectPath, string configuration, string workingDirectory)
    {
        RunProcess(
            fileName: "dotnet",
            arguments: $"build \"{projectPath}\" -c {configuration}",
            workingDirectory);
    }

    private static void RunBash(string scriptPath, string packagePath, string workingDirectory)
    {
        RunProcess(
            fileName: "bash",
            arguments: $"\"{scriptPath}\" \"{packagePath}\"",
            workingDirectory);
    }

    private const int ProcessTimeoutMilliseconds = 120_000;

    private static void RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start process '{fileName}'.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(ProcessTimeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited on its own between the timeout expiring and the kill attempt.
            }

            // The kill closes the redirected streams, so the pending reads now complete.
            process.WaitForExit();

            var timedOutStandardOutput = standardOutputTask.GetAwaiter().GetResult();
            var timedOutStandardError = standardErrorTask.GetAwaiter().GetResult();

            Assert.Fail(
                $"Command '{fileName} {arguments}' did not exit within {ProcessTimeoutMilliseconds} ms and was terminated.{Environment.NewLine}STDOUT:{Environment.NewLine}{timedOutStandardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{timedOutStandardError}");
        }

        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();

        Assert.True(
            process.ExitCode == 0,
            $"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{standardOutput}{Environment.NewLine}STDERR:{Environment.NewLine}{standardError}");
    }

    private static void AssertPackageContents(ZipArchive packageArchive)
    {
        var packagedFiles = packageArchive.Entries
            .Where(static entry => !string.IsNullOrEmpty(entry.Name))
            .Select(static entry => entry.FullName)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        var packagedPayloadFiles = packagedFiles
            .Where(IsFunctionalPayloadPath)
            .ToArray();

        Assert.Equal(ExpectedPayloadFiles, packagedPayloadFiles);

        var unexpectedFiles = packagedFiles
            .Where(static path => !IsAllowedPackagePath(path))
            .ToArray();

        Assert.Empty(unexpectedFiles);
        AssertRuntimeExposesComposableAttribute(packageArchive);

        Assert.DoesNotContain(
            packagedFiles,
            static path => Path.GetFileName(path).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                && path.EndsWith(".dll", StringComparison.Ordinal));

        Assert.NotNull(packageArchive.GetEntry(PackageReadmePath));

        var nuspecEntry = packageArchive.GetEntry($"{PackageId}.nuspec");

        Assert.NotNull(nuspecEntry);

        var nuspecDocument = LoadXml(nuspecEntry);
        var packageNamespace = nuspecDocument.Root!.Name.Namespace;
        var metadata = nuspecDocument.Root.Element(packageNamespace + "metadata");

        Assert.NotNull(metadata);
        Assert.Equal(PackageId, metadata.Element(packageNamespace + "id")?.Value);
        Assert.Equal(PackageVersion, metadata.Element(packageNamespace + "version")?.Value);
        Assert.Equal(PackageReadmePath, metadata.Element(packageNamespace + "readme")?.Value);

        var dependencyElements = metadata
            .Descendants()
            .Where(static element => element.Name.LocalName == "dependency")
            .ToArray();

        // Runtime depends on the granular Microsoft.AspNetCore.Components package instead of
        // the unconditional Microsoft.AspNetCore.App FrameworkReference, which had no
        // browser-wasm runtime pack and broke Blazor WebAssembly consumers (issue #23).
        var dependency = Assert.Single(dependencyElements);

        Assert.Equal("Microsoft.AspNetCore.Components", dependency.Attribute("id")?.Value);
    }

    private static void AssertRuntimeExposesComposableAttribute(ZipArchive packageArchive)
    {
        var runtimeEntry = packageArchive.GetEntry("lib/net10.0/BlazorCompose.Runtime.dll");

        Assert.NotNull(runtimeEntry);

        using var entryStream = runtimeEntry.Open();
        using var memoryStream = new MemoryStream();
        entryStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        using var peReader = new PEReader(memoryStream);
        var metadataReader = peReader.GetMetadataReader();
        var packagedTypeNames = metadataReader.TypeDefinitions
            .Select(handle =>
            {
                var typeDefinition = metadataReader.GetTypeDefinition(handle);

                return (
                    metadataReader.GetString(typeDefinition.Namespace),
                    metadataReader.GetString(typeDefinition.Name));
            })
            .ToArray();

        Assert.Contains(
            ("BlazorCompose", "ComposableAttribute"),
            packagedTypeNames);
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memoryStream = new MemoryStream();

        stream.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();

        return XDocument.Load(stream);
    }

    private static bool IsFunctionalPayloadPath(string path)
        => path.StartsWith("analyzers/", StringComparison.Ordinal)
            || path.StartsWith("lib/", StringComparison.Ordinal)
            || path.StartsWith("build/", StringComparison.Ordinal)
            || path.StartsWith("buildTransitive/", StringComparison.Ordinal)
            || path.StartsWith("contentFiles/", StringComparison.Ordinal)
            || path.StartsWith("tools/", StringComparison.Ordinal)
            || path.StartsWith("runtimes/", StringComparison.Ordinal);

    private static bool IsAllowedPackagePath(string path)
    {
        if (path is "[Content_Types].xml" or "_rels/.rels" or PackageReadmePath or $"{PackageId}.nuspec")
        {
            return true;
        }

        if (path.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
            && path.EndsWith(".psmdcp", StringComparison.Ordinal))
        {
            return true;
        }

        return ExpectedPayloadFiles.Contains(path, StringComparer.Ordinal);
    }
}

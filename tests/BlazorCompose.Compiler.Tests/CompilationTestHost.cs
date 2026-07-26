using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorCompose.Compiler.Tests;

/// <summary>Encapsulates the output of a single generator run for assertion in tests.</summary>
public sealed record GeneratorRunResult(
    GeneratorDriver Driver,
    Compilation OutputCompilation,
    ImmutableArray<GeneratedSourceResult> GeneratedSources,
    IReadOnlyDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> TrackedSteps,
    ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// Hosts in-memory Roslyn compilations for generator and analyzer tests.
/// The driver is created with <see cref="GeneratorDriverOptions.TrackIncrementalGeneratorSteps"/>
/// enabled and the updated driver is returned so that callers can reuse it for incremental runs.
/// </summary>
public static class CompilationTestHost
{
    /// <summary>
    /// Parses <paramref name="source"/> as a single file, creates a test compilation, runs
    /// <see cref="BlazorComposeGenerator"/>, and returns the updated driver together with all
    /// generated sources, tracked incremental steps, the updated compilation, and generator diagnostics.
    /// </summary>
    public static GeneratorRunResult RunGenerator(string source) =>
        RunGenerator(("Test.cs", source));

    /// <summary>
    /// Parses each <c>(Path, Source)</c> tuple into its own syntax tree, creates a test compilation, and
    /// runs the generator.  Multiple files let cross-file expansion semantics (definition in one file,
    /// call site in another) be exercised.
    /// </summary>
    public static GeneratorRunResult RunGenerator(params (string Path, string Source)[] sources)
    {
        var syntaxTrees = sources
            .Select(static source => CSharpSyntaxTree.ParseText(
                source.Source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                path: source.Path))
            .ToArray();

        var compilation = CreateCompilation(syntaxTrees);
        return RunGenerator(compilation);
    }

    /// <summary>Runs the generator against a pre-built compilation and collects its results.</summary>
    internal static GeneratorRunResult RunGenerator(CSharpCompilation compilation)
    {
        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorComposeGenerator().AsSourceGenerator()],
            driverOptions: driverOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        var generatedSources = runResult.Results
            .SelectMany(static r => r.GeneratedSources)
            .ToImmutableArray();

        IReadOnlyDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> trackedSteps =
            runResult.Results.Length > 0
                ? runResult.Results[0].TrackedSteps
                : [];

        return new GeneratorRunResult(driver, outputCompilation, generatedSources, trackedSteps, diagnostics);
    }

    /// <summary>
    /// Asserts that the post-generation <see cref="GeneratorRunResult.OutputCompilation"/> contains no C#
    /// error diagnostics, so a supported component's generated <c>RenderView</c> is verified to actually
    /// compile rather than only inspected as text.  On failure every error is included in the message to
    /// make the emitted mistake (for example a CS0103 out-of-scope name or a CS0664 mistyped literal)
    /// immediately visible.
    /// </summary>
    public static void AssertOutputCompiles(GeneratorRunResult result)
    {
        var errors = result.OutputCompilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        Assert.True(
            errors.IsEmpty,
            "Generated output failed to compile: " +
            string.Join(
                "; ",
                errors.Select(static error => error.ToString())));
    }

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and returns it as a metadata
    /// reference so a consuming compilation can reference a <c>[Composable]</c> method that exists only in
    /// metadata (no source declaration), exercising the metadata-only expansion diagnostic path.
    /// </summary>
    public static MetadataReference CompileToMetadataReference(string source, string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14));

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: [syntaxTree],
            references: BuildMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            "Metadata reference source failed to compile: " +
            string.Join("; ", emitResult.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

        stream.Seek(0, SeekOrigin.Begin);
        return MetadataReference.CreateFromStream(stream);
    }

    /// <summary>
    /// Creates a compilation from raw <c>(Path, Source)</c> tuples plus additional metadata references,
    /// letting tests wire a metadata-only composable definition into the consuming compilation.
    /// </summary>
    internal static CSharpCompilation CreateCompilation(
        (string Path, string Source)[] sources,
        params MetadataReference[] extraReferences)
    {
        var syntaxTrees = sources
            .Select(static source => CSharpSyntaxTree.ParseText(
                source.Source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                path: source.Path))
            .ToArray();

        var references = BuildMetadataReferences().AddRange(extraReferences);

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Parses <paramref name="source"/>, creates a test compilation, runs the analyzer
    /// <typeparamref name="T"/>, and returns the analyzer-owned diagnostics only.
    /// </summary>
    public static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync<T>(string source)
        where T : DiagnosticAnalyzer, new()
    {
        var compilation = CreateCompilation(source);
        var analyzer = new T();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            [analyzer]);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    internal static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14));

        return CreateCompilation(syntaxTree);
    }

    internal static CSharpCompilation CreateCompilation(params SyntaxTree[] syntaxTrees) =>
        CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: BuildMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// Builds the metadata references shared by every test compilation: the host process's trusted
    /// platform assemblies plus <c>Microsoft.AspNetCore.Components</c>.  When <paramref name="includeRuntime"/>
    /// is <see langword="true"/> (the default) the <c>BlazorCompose.Runtime</c> assembly is also referenced;
    /// pass <see langword="false"/> when the test defines the <c>BlazorCompose</c> types in-source and must
    /// not pull in the compiled runtime.
    /// </summary>
    internal static ImmutableArray<MetadataReference> BuildMetadataReferences(bool includeRuntime = true)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(string path)
        {
            if (!string.IsNullOrEmpty(path) && seen.Add(path) && File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        // BCL and shared-framework assemblies available to the host process
        foreach (var path in ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
                     .Split(Path.PathSeparator))
        {
            Add(path);
        }

        // BlazorCompose.Runtime (provides ComposeComponentBase, View, Html)
        if (includeRuntime)
            Add(typeof(BlazorCompose.ComposeComponentBase).Assembly.Location);

        // Microsoft.AspNetCore.Components (provides ComponentBase, RenderTreeBuilder)
        Add(typeof(ComponentBase).Assembly.Location);

        return [.. references];
    }
}

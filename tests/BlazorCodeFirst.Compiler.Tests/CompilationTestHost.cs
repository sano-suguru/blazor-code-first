using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace BlazorCodeFirst.Compiler.Tests;

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
    /// <summary>One reference set per flag combination, built on first request.</summary>
    private static readonly ConcurrentDictionary<
        (bool IncludeRuntime, bool IncludeComponentsWeb),
        ImmutableArray<MetadataReference>> ReferenceSets = new();

    /// <summary>
    /// One <see cref="MetadataReference"/> per assembly file, so those sets share theirs instead of
    /// decoding the same ~200 files once each. Roslyn keys its assembly-symbol cache on metadata identity,
    /// so a second reference to identical content also costs a second bind in the first compilation that
    /// uses it.
    /// </summary>
    private static readonly ConcurrentDictionary<string, MetadataReference> ReferencesByPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses <paramref name="source"/> as a single file, creates a test compilation, runs
    /// <see cref="BlazorCodeFirstGenerator"/>, and returns the updated driver together with all
    /// generated sources, tracked incremental steps, the updated compilation, and generator diagnostics.
    /// </summary>
    public static GeneratorRunResult RunGenerator(string source) =>
        RunGenerator(("Test.cs", source));

    /// <summary>
    /// Parses each <c>(Path, Source)</c> tuple into its own syntax tree, creates a test compilation, and
    /// runs the generator. Multiple files let cross-file expansion semantics (definition in one file,
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
    internal static GeneratorRunResult RunGenerator(CSharpCompilation compilation) =>
        RunGeneratorCore(compilation, additionalTexts: [], optionsProvider: null);

    /// <summary>
    /// Parses each <c>(Path, Source)</c> tuple as in <see cref="RunGenerator(ValueTuple{string,string}[])"/>,
    /// and additionally supplies one <see cref="AdditionalText"/> per <paramref name="cssScopes"/> entry
    /// with its <c>CssScope</c> value visible through <c>build_metadata.AdditionalFiles.CssScope</c>, the
    /// same key <c>BlazorCodeFirst.Build</c> stamps in a real build. Lets a test exercise scope resolution
    /// without a nested <c>dotnet build</c>.
    /// </summary>
    internal static GeneratorRunResult RunGeneratorWithCssScopes(
        (string Path, string Source)[] sources, (string CssPath, string Scope)[] cssScopes)
    {
        var syntaxTrees = sources
            .Select(static source => CSharpSyntaxTree.ParseText(
                source.Source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                path: source.Path))
            .ToArray();

        var compilation = CreateCompilation(syntaxTrees);

        ImmutableArray<AdditionalText> additionalTexts = [.. cssScopes
            .Select(static entry => (AdditionalText)new TestAdditionalText(entry.CssPath))];

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            cssScopes.ToDictionary(static entry => entry.CssPath, static entry => entry.Scope));

        return RunGeneratorCore(compilation, additionalTexts, optionsProvider);
    }

    private static GeneratorRunResult RunGeneratorCore(
        CSharpCompilation compilation,
        ImmutableArray<AdditionalText> additionalTexts,
        AnalyzerConfigOptionsProvider? optionsProvider)
    {
        var driverOptions = new GeneratorDriverOptions(
            disabledOutputs: default,
            trackIncrementalGeneratorSteps: true);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new BlazorCodeFirstGenerator().AsSourceGenerator()],
            additionalTexts: additionalTexts,
            parseOptions: null,
            optionsProvider: optionsProvider,
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
    /// Runs the generator on <paramref name="source"/> and returns the single component's
    /// <see cref="ComponentModel"/>, extracted from the tracked <c>ComponentModeling</c> incremental step
    /// (the same idiom <see cref="IncrementalGeneratorTests"/> uses to identify a component by name). Fails
    /// loudly if the source does not resolve to exactly one modeled component.
    /// </summary>
    internal static ComponentModel ModelSingleComponent(string source)
    {
        var result = RunGenerator(source);
        Assert.True(result.TrackedSteps.ContainsKey("ComponentModeling"),
            "Expected tracked step 'ComponentModeling' but found: " +
            string.Join(", ", result.TrackedSteps.Keys));

        var models = result.TrackedSteps["ComponentModeling"]
            .SelectMany(static step => step.Outputs)
            .Select(static output => ((ComponentModelResult)output.Value).Model)
            .Where(static model => model is not null)
            .ToImmutableArray();

        return Assert.Single(models)!;
    }

    /// <summary>
    /// Asserts that the post-generation <see cref="GeneratorRunResult.OutputCompilation"/> contains no C#
    /// error diagnostics, so a supported component's generated <c>RenderView</c> is verified to actually
    /// compile rather than only inspected as text. On failure every error is included in the message to
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
    /// Asserts that nothing in <paramref name="result"/>'s compilation warned about nullability — the
    /// author's own file included, which is what separates this from
    /// <see cref="AssertGeneratedOutputHasNoWarnings"/>.
    /// </summary>
    /// <remarks>
    /// What a widened surface parameter is for. Passing a <c>string?</c> to a <see langword="string"/>
    /// parameter is CS8604 and passing a <see langword="null"/> literal is CS8625: warnings, so a test that
    /// asks only for errors passes whether the parameter was widened or not (#171). The three nullable
    /// warnings are named rather than every warning, because a test's input source may legitimately warn
    /// about something else — an unassigned field, a never-read local.
    /// </remarks>
    public static void AssertNoNullableWarnings(GeneratorRunResult result)
    {
        var warnings = result.OutputCompilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Id is "CS8600" or "CS8604" or "CS8625")
            .ToImmutableArray();

        Assert.True(
            warnings.IsEmpty,
            "Nullability warned: " +
            string.Join("; ", warnings.Select(static warning => warning.ToString())));
    }

    /// <summary>
    /// Asserts that the <em>generated</em> trees produced no warnings. Stricter than
    /// <see cref="AssertOutputCompiles"/>, which looks only at errors, and narrower than asking the whole
    /// compilation: a test's own input source is allowed to warn (an unassigned field, an unused local),
    /// and only the generated file is this generator's to keep clean.
    /// </summary>
    /// <remarks>
    /// Worth its own assertion because this repository builds with <c>TreatWarningsAsErrors</c>
    /// (<c>Directory.Build.props</c>), so a warning inside generated code is a build failure for any
    /// consumer that does the same — and one the consumer cannot fix, because they do not write that file.
    /// #235 was exactly that shape and went unnoticed because every existing assertion about generated
    /// code stopped at errors.
    /// </remarks>
    public static void AssertGeneratedOutputHasNoWarnings(GeneratorRunResult result)
    {
        var generatedTrees = result.GeneratedSources
            .Select(static source => source.SyntaxTree)
            .ToImmutableHashSet();

        var warnings = result.OutputCompilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)
            .Where(diagnostic =>
                diagnostic.Location.SourceTree is { } tree && generatedTrees.Contains(tree))
            .ToImmutableArray();

        Assert.True(
            warnings.IsEmpty,
            "Generated output warned: " +
            string.Join("; ", warnings.Select(static warning => warning.ToString())));
    }

    /// <summary>
    /// Asserts that the generator reported nothing at all for <paramref name="result"/>. Companion to
    /// <see cref="AssertOutputCompiles"/>: that one proves the emitted code compiles, this one proves the
    /// input needed no complaint. On failure every diagnostic is named, so a test that expected a clean run
    /// says which rule fired instead.
    /// </summary>
    public static void AssertNoDiagnostics(GeneratorRunResult result)
    {
        Assert.True(
            result.Diagnostics.IsEmpty,
            "Expected no BlazorCodeFirst diagnostics but got: " +
            string.Join("; ", result.Diagnostics.Select(static d => d.ToString())));
    }

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and returns it as a metadata
    /// reference so a consuming compilation can reference a <c>[ViewPart]</c> method that exists only in
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
    /// letting tests wire a metadata-only view part definition into the consuming compilation.
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
    public static Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync<T>(string source)
        where T : DiagnosticAnalyzer, new() =>
        RunAnalyzerAsync<T>(CreateCompilation(source));

    /// <summary>
    /// Runs the analyzer <typeparamref name="T"/> against a pre-built compilation and returns the
    /// analyzer-owned diagnostics only, so a test can supply a compilation whose <c>BlazorCodeFirst</c>
    /// surface is declared in-source (see <see cref="CreateCompilationWithoutRuntime"/>) rather than
    /// referenced.
    /// </summary>
    internal static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync<T>(CSharpCompilation compilation)
        where T : DiagnosticAnalyzer, new()
    {
        var compilationWithAnalyzers = compilation.WithAnalyzers([new T()]);
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        // An analyzer that throws does not fail the run: Roslyn catches the exception and reports it as
        // AD0001, a warning, and the analyzer's own diagnostics are simply absent from that point on.
        // Every negative test here asserts the absence of one ID, so a crash reads as the exemption it was
        // written to check. Failing on AD0001 is what makes those assertions mean what they say.
        if (diagnostics.FirstOrDefault(static d => d.Id == "AD0001") is { } crash)
            Assert.Fail(crash.GetMessage());

        return diagnostics;
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

    /// <summary>
    /// Creates a compilation from raw <c>(Path, Source)</c> tuples that does <em>not</em> reference
    /// <c>BlazorCodeFirst.Runtime</c>, for tests that declare the <c>BlazorCodeFirst</c> surface in-source.
    /// </summary>
    internal static CSharpCompilation CreateCompilationWithoutRuntime(
        params (string Path, string Source)[] sources)
    {
        var syntaxTrees = sources
            .Select(static source => CSharpSyntaxTree.ParseText(
                source.Source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                path: source.Path))
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: BuildMetadataReferences(includeRuntime: false),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static CSharpCompilation CreateCompilation(params SyntaxTree[] syntaxTrees) =>
        CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: BuildMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    /// <summary>
    /// Creates a compilation from raw <c>(Path, Source)</c> tuples that does <em>not</em> reference
    /// <c>Microsoft.AspNetCore.Components.Web</c>, the assembly carrying the framework's own
    /// <c>[EventHandler]</c> registrations. Nothing else about the surface changes: <c>ChangeEventArgs</c>
    /// and the rest of the argument types this reaches for are declared in
    /// <c>Microsoft.AspNetCore.Components</c>, which stays referenced, so a body that names one still
    /// compiles and only the framework's registrations are gone. A registration the compilation declares
    /// itself is read here as it is anywhere, which is what #396 separated.
    /// </summary>
    internal static CSharpCompilation CreateCompilationWithoutComponentsWeb(
        params (string Path, string Source)[] sources)
    {
        var syntaxTrees = sources
            .Select(static source => CSharpSyntaxTree.ParseText(
                source.Source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14),
                path: source.Path))
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: BuildMetadataReferences(includeComponentsWeb: false),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Returns the metadata references shared by every test compilation: the host process's trusted
    /// platform assemblies plus <c>Microsoft.AspNetCore.Components</c> and
    /// <c>Microsoft.AspNetCore.Components.Web</c>. When <paramref name="includeRuntime"/>
    /// is <see langword="true"/> (the default) the <c>BlazorCodeFirst.Runtime</c> assembly is also referenced;
    /// pass <see langword="false"/> when the test defines the <c>BlazorCodeFirst</c> types in-source and must
    /// not pull in the compiled runtime. Pass <paramref name="includeComponentsWeb"/> as
    /// <see langword="false"/> to build the compilation an author gets without the framework's
    /// <c>[EventHandler]</c> registrations, which is what BCF3028 skips in silence for the events they
    /// name. Each flag combination is built on first request;
    /// every later call for it gets the same references back.
    /// </summary>
    /// <remarks>
    /// What the flags select is fixed before any test runs: the host process's TPA list and three assembly
    /// locations. Building it per call meant splitting that list, stat-ing every entry, and reading every
    /// surviving file's metadata again for every case in this suite (#217). Sharing the results is safe
    /// because <see cref="MetadataReference"/> instances are immutable, which the callers already relied on
    /// by handing one returned set to several compilations.
    /// </remarks>
    internal static ImmutableArray<MetadataReference> BuildMetadataReferences(
        bool includeRuntime = true, bool includeComponentsWeb = true) =>
        ReferenceSets.GetOrAdd(
            (includeRuntime, includeComponentsWeb),
            static key => CreateMetadataReferences(key.IncludeRuntime, key.IncludeComponentsWeb));

    private static ImmutableArray<MetadataReference> CreateMetadataReferences(
        bool includeRuntime, bool includeComponentsWeb)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void Add(string path)
        {
            if (!string.IsNullOrEmpty(path) && seen.Add(path) && File.Exists(path))
                references.Add(ReferencesByPath.GetOrAdd(path, static p => MetadataReference.CreateFromFile(p)));
        }

        var runtimeAssemblyPath = typeof(BlazorCodeFirst.BodyComponentBase).Assembly.Location;
        var componentsWebAssemblyPath = typeof(Microsoft.AspNetCore.Components.Web.EventHandlers)
            .Assembly.Location;

        // BCL and shared-framework assemblies available to the host process. The runtime is skipped here
        // when it is not wanted: this test project references BlazorCodeFirst.Runtime, so its own deps.json
        // lists BlazorCodeFirst.Runtime.dll as a runtime asset and the assembly is in TPA. Without this
        // filter the conditional Add below never excluded anything, the runtime came in through TPA either
        // way, and an in-source shim only won by CS0436 source shadowing rather than by isolation.
        // Microsoft.AspNetCore.Components.Web arrives the same way and is filtered for the same reason.
        foreach (var path in ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
                     .Split(Path.PathSeparator))
        {
            if (!includeRuntime && IsSameAssemblyFile(path, runtimeAssemblyPath))
                continue;

            if (!includeComponentsWeb && IsSameAssemblyFile(path, componentsWebAssemblyPath))
                continue;

            Add(path);
        }

        // BlazorCodeFirst.Runtime (provides BodyComponentBase, View, Html)
        if (includeRuntime)
            Add(runtimeAssemblyPath);

        // Microsoft.AspNetCore.Components (provides ComponentBase, RenderTreeBuilder)
        Add(typeof(ComponentBase).Assembly.Location);

        // Microsoft.AspNetCore.Components.Web (provides the [EventHandler] event → argument type table and
        // the MouseEventArgs family it names). Added explicitly rather than left to TPA so that a
        // compilation an ordinary Blazor consumer would have is what these tests compile against.
        if (includeComponentsWeb)
            Add(componentsWebAssemblyPath);

        return [.. references];
    }

    /// <summary>
    /// Whether two probe paths name the same assembly file. Compared by file name rather than by full
    /// path: TPA and <c>Assembly.Location</c> are the same string today, but they are produced by different
    /// mechanisms (a build-time list versus the loaded assembly), and a filter that silently stops matching
    /// would restore the isolation defect without failing anything.
    /// </summary>
    private static bool IsSameAssemblyFile(string path, string other) =>
        string.Equals(
            Path.GetFileName(path), Path.GetFileName(other), StringComparison.OrdinalIgnoreCase);
}

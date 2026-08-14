using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace BlazorCodeFirst.TrimTests;

/// <summary>
/// Inspects the trimmed output of <c>BlazorCodeFirst.TrimTestApp</c> using System.Reflection.Metadata
/// to verify that the trimmer behaves according to the architecture's expectations:
/// - Generated <c>RenderView</c> must be retained (it is rooted by <c>BuildRenderTree</c>).
/// - The <c>Body</c> getter should be trimmed from both derived and base types (no runtime caller).
/// - The layout counterpart <c>Chrome</c> should be trimmed on the same terms: it is the same inert
///   design-time getter, so the contract has to hold for <c>ChromeLayoutBase</c> too.
/// - Unreferenced inert members of <c>BlazorCodeFirst.Html</c> and the <c>ComponentView&lt;T&gt;</c>
///   builder type should be trimmed.
/// - <c>Decorations.Bind</c> and <c>ComponentView&lt;T&gt;.Bind</c>, the two-way binding surface, are
///   inert on the same terms and should be trimmed too.
/// - <c>ComponentView&lt;T&gt;.Template</c>, the generic fragment channel, is inert on the same terms:
///   the generator emits the <c>RenderFragment&lt;TContext&gt;</c> lambda itself, so no runtime caller
///   remains and nothing generic is constructed reflectively.
/// </summary>
public sealed class TrimmedOutputTests
{
    private const string AppAssemblyFileName = "BlazorCodeFirst.TrimTestApp.dll";
    private const string RuntimeAssemblyFileName = "BlazorCodeFirst.Runtime.dll";
    private const string ComponentsAssemblyFileName = "Microsoft.AspNetCore.Components.dll";

    /// <summary>
    /// The one method this surface reaches only through reflection, and therefore the one the trimmer
    /// could take away without a warning. <c>BindConverter.ParserDelegateCache</c> pulls
    /// <c>ConvertToEnum</c> out with <c>GetMethod(...).MakeGenericMethod(...)</c> when an enum is bound
    /// (#307), and <c>DynamicallyAccessedMembers</c> sits on the generic parameter rather than on this
    /// private method, so nothing in the annotations states that it must survive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decision to use the framework's parse path instead of writing <c>Enum.TryParse&lt;T&gt;</c>
    /// into generated code was made conditional on this measurement: an absence here means an enum
    /// binding throws at the first event in a trimmed app, and the emitter would have to convert enums
    /// itself. TrimCounter binds a <c>DayOfWeek</c> so that the question is asked at all.
    /// </para>
    /// <para>
    /// It survives because binding anything through the converter roots the converter whole, not because
    /// the trimmer reasoned about the reflective call. Measured on 2026-08-14 by publishing this app with
    /// and without its value-type bindings: <c>BindConverter</c> keeps 28 methods with only the
    /// <see langword="string"/> and <see langword="bool"/> bindings and 53 with the rest, and
    /// <c>Microsoft.AspNetCore.Components.dll</c> goes from 71,680 to 81,408 bytes (osx-arm64,
    /// self-contained, <c>TrimMode=full</c>). So this assertion is not measuring the trimmer's analysis
    /// of the reflection; it is a guard against a framework change that made the method trimmable, which
    /// would be silent — <c>ILLinkTreatWarningsAsErrors</c> catches an unsuppressed reflection warning,
    /// and this call site is suppressed.
    /// </para>
    /// </remarks>
    [Fact]
    public void TrimmedComponents_AfterPublish_RetainsTheEnumParserTheBindingReachesReflectively()
    {
        var componentsAssemblyPath = ResolvePublishedAssembly(ComponentsAssemblyFileName);

        var methods = GetMethodNames(
            componentsAssemblyPath, "BindConverter", expectedNamespace: "Microsoft.AspNetCore.Components");

        Assert.Contains("ConvertToEnum", methods);
        Assert.Contains("ConvertToNullableEnum", methods);
    }

    private static readonly string? TrimOutputDirectory =
        Environment.GetEnvironmentVariable("BLAZORCODEFIRST_TRIM_OUTPUT");

    [Fact]
    public void TrimmedApp_AfterPublish_RetainsRenderViewMethod()
    {
        var appAssemblyPath = ResolvePublishedAssembly(AppAssemblyFileName);

        var methods = GetMethodNames(appAssemblyPath, "TrimCounter", expectedNamespace: "");
        Assert.Contains("RenderView", methods);
    }

    [Fact]
    public void TrimmedApp_AfterPublish_TrimsBodyGetter()
    {
        var appAssemblyPath = ResolvePublishedAssembly(AppAssemblyFileName);

        var methods = GetMethodNames(appAssemblyPath, "TrimCounter", expectedNamespace: "");

        // The Body getter should be trimmed since RenderView (generated) doesn't call it
        // and no other code in the app invokes it.
        Assert.DoesNotContain("get_Body", methods);
    }

    [Fact]
    public void TrimmedApp_AfterPublish_TrimsPrivateViewPartMethod()
    {
        var appAssemblyPath = ResolvePublishedAssembly(AppAssemblyFileName);

        var methods = GetMethodNames(
            appAssemblyPath,
            "TrimCounter",
            expectedNamespace: "");

        Assert.DoesNotContain("CountLabel", methods);
    }

    [Fact]
    public void TrimmedApp_AfterPublish_RetainsLayoutRenderViewMethod()
    {
        var appAssemblyPath = ResolvePublishedAssembly(AppAssemblyFileName);

        var methods = GetMethodNames(appAssemblyPath, "TrimLayout", expectedNamespace: "");
        Assert.Contains("RenderView", methods);
    }

    [Fact]
    public void TrimmedApp_AfterPublish_TrimsLayoutChromeGetter()
    {
        var appAssemblyPath = ResolvePublishedAssembly(AppAssemblyFileName);

        var methods = GetMethodNames(appAssemblyPath, "TrimLayout", expectedNamespace: "");

        // Chrome is the layout's design-time getter and has no runtime caller, exactly as Body has
        // none on a component. RenderView above proves the type itself survived, so this absence is
        // the trimmer removing the getter rather than the whole layout.
        Assert.DoesNotContain("get_Chrome", methods);
    }

    [Fact]
    public void TrimmedApp_AfterPublish_TrimsLayoutViewPartMethod()
    {
        var appAssemblyPath = ResolvePublishedAssembly(AppAssemblyFileName);

        var methods = GetMethodNames(appAssemblyPath, "TrimLayout", expectedNamespace: "");

        // A [ViewPart] called from Chrome is statically expanded into RenderView, so the method
        // itself is unreachable, the same result the component path asserts for CountLabel.
        Assert.DoesNotContain("ChromeTitle", methods);
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_TrimsBaseChromeGetter()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);

        var methods = GetMethodNames(runtimeAssemblyPath, "ChromeLayoutBase", expectedNamespace: "BlazorCodeFirst");

        // The abstract Chrome getter in the layout base should be trimmed for the same reason as
        // BodyComponentBase.Body: nothing calls it at runtime.
        Assert.DoesNotContain("get_Chrome", methods);
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_RetainsChromeLayoutBaseBuildRenderTree()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);

        var methods = GetMethodNames(runtimeAssemblyPath, "ChromeLayoutBase", expectedNamespace: "BlazorCodeFirst");

        // BuildRenderTree is the root that keeps the layout's rendering chain alive.
        Assert.Contains("BuildRenderTree", methods);
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_TrimsBaseBodyGetter()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);

        var methods = GetMethodNames(runtimeAssemblyPath, "BodyComponentBase", expectedNamespace: "BlazorCodeFirst");

        // The abstract Body getter in the base class should also be trimmed, no runtime call path.
        Assert.DoesNotContain("get_Body", methods);
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_TrimsUnreferencedHtmlMembers()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);

        var methods = GetMethodNames(runtimeAssemblyPath, "Html", expectedNamespace: "BlazorCodeFirst");

        // The tested Html members are unreachable at runtime, the source generator inlines their
        // semantics into RenderView via direct RenderTreeBuilder calls. Div/Span/Button are properties
        // since #100, so their MethodDef names are the compiler-generated getters (get_Div, etc.), not
        // the bare property names, asserting the bare names would be vacuous, since those were never
        // MethodDef names to begin with.
        Assert.DoesNotContain("get_Div", methods);
        Assert.DoesNotContain("get_Span", methods);
        Assert.DoesNotContain("get_Button", methods);
        Assert.DoesNotContain("Element", methods);
        Assert.DoesNotContain("If", methods);
        Assert.DoesNotContain("ForEach", methods);
        Assert.DoesNotContain("Component", methods);
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_TrimsUnreferencedDecorationsBindMethod()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);

        var methods = GetMethodNames(runtimeAssemblyPath, "Decorations", expectedNamespace: "BlazorCodeFirst");

        // Bind is inert design-time syntax like every other Decorations member (Class, OnClick, Attr,
        // …): the generator reads the getter/setter lambdas statically and folds them into
        // AddAttribute/AddAttributeEventHandler/SetUpdatesAttributeName calls in RenderView, so nothing
        // at runtime ever calls Decorations.Bind. All six overloads (string/bool, getter-only, explicit
        // setter, explicit async setter) share the metadata name "Bind", so this single assertion
        // covers every one of them, provided TrimTestApp's Body reaches each shape (it does; see
        // TrimCounter.Body).
        Assert.DoesNotContain("Bind", methods);
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_TrimsComponentViewBindMethod()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);

        // ComponentView<T>'s metadata TypeDef.Name carries the generic arity suffix, same as the
        // whole-type check below.
        var methods = GetMethodNames(runtimeAssemblyPath, "ComponentView`1", expectedNamespace: "BlazorCodeFirst");

        // Bind is inert design-time syntax on ComponentView<T>, the same contract as Param and the
        // indexer: the generator reads the selector/getter/setter lambdas statically and never calls
        // it at runtime. All three overloads (getter-only, explicit setter, explicit async setter)
        // share the metadata name "Bind", so this single assertion covers every one of them, provided
        // TrimTestApp's Body reaches each shape (it does; see TrimCounter.Body).
        Assert.DoesNotContain("Bind", methods);
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_TrimsComponentViewTemplateMethod()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);
        var methods = GetMethodNames(runtimeAssemblyPath, "ComponentView`1", "BlazorCodeFirst");
        Assert.DoesNotContain("Template", methods);
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_TrimsComponentViewBuilderType()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);

        // ComponentView<T> is an inert design-time builder with no runtime caller, so the whole type
        // should be trimmed. Its metadata TypeDef.Name is "ComponentView`1" (backtick + arity); assert
        // no type whose name starts with "ComponentView" survives in the BlazorCodeFirst namespace.
        Assert.False(
            HasTypeStartingWith(runtimeAssemblyPath, "ComponentView", expectedNamespace: "BlazorCodeFirst"),
            "ComponentView<T> builder type should be trimmed from the runtime assembly.");
    }

    [Fact]
    public void TrimmedRuntime_AfterPublish_RetainsBodyComponentBaseBuildRenderTree()
    {
        var runtimeAssemblyPath = ResolvePublishedAssembly(RuntimeAssemblyFileName);

        var methods = GetMethodNames(runtimeAssemblyPath, "BodyComponentBase", expectedNamespace: "BlazorCodeFirst");

        // BuildRenderTree is the root that keeps the rendering chain alive.
        Assert.Contains("BuildRenderTree", methods);
    }

    /// <summary>
    /// Gets method definition names (metadata-level) for a given type in the assembly.
    /// Matches type by both namespace and short name to avoid ambiguity.
    /// A method removed by the trimmer will not have a MethodDef row at all.
    /// </summary>
    /// <param name="assemblyPath">Path to the PE assembly to inspect.</param>
    /// <param name="typeName">Short type name to match.</param>
    /// <param name="expectedNamespace">
    /// Expected namespace for the type. Use empty string for global/anonymous namespace (top-level statements).
    /// </param>
    private static HashSet<string> GetMethodNames(string assemblyPath, string typeName, string expectedNamespace)
    {
        using var fileStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(fileStream);
        var metadataReader = peReader.GetMetadataReader();

        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            var name = metadataReader.GetString(typeDef.Name);
            var ns = metadataReader.GetString(typeDef.Namespace);

            if (!string.Equals(name, typeName, StringComparison.Ordinal) ||
                !string.Equals(ns, expectedNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                var methodName = metadataReader.GetString(methodDef.Name);
                result.Add(methodName);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns whether any type in <paramref name="expectedNamespace"/> has a metadata name starting with
    /// <paramref name="namePrefix"/>. Used for generic types whose metadata name carries a `arity suffix
    /// (e.g. "ComponentView`1"), where an exact-name match would silently miss.
    /// </summary>
    private static bool HasTypeStartingWith(string assemblyPath, string namePrefix, string expectedNamespace)
    {
        using var fileStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(fileStream);
        var metadataReader = peReader.GetMetadataReader();

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            var name = metadataReader.GetString(typeDef.Name);
            var ns = metadataReader.GetString(typeDef.Namespace);

            if (name.StartsWith(namePrefix, StringComparison.Ordinal) &&
                string.Equals(ns, expectedNamespace, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a published assembly under the trim output directory, asserting that both the output
    /// directory (the architecture gate) and the assembly itself exist. A missing publish therefore
    /// fails with a clear, assembly-specific "not found" message instead of a downstream file-open error.
    /// </summary>
    private static string ResolvePublishedAssembly(string assemblyFileName)
    {
        EnsureOutputDirectoryExists();

        var assemblyPath = Path.Combine(TrimOutputDirectory!, assemblyFileName);
        Assert.True(File.Exists(assemblyPath), $"Published assembly '{assemblyFileName}' not found at: {assemblyPath}");

        return assemblyPath;
    }

    /// <summary>
    /// Asserts that the trim output directory is set and exists. This is an architecture gate,
    /// missing output is a hard failure, not a skippable condition.
    /// </summary>
    private static void EnsureOutputDirectoryExists()
    {
        Assert.False(
            string.IsNullOrEmpty(TrimOutputDirectory),
            "BLAZORCODEFIRST_TRIM_OUTPUT environment variable is not set. " +
            "This is an architecture gate: publish the TrimTestApp first, then run tests " +
            "with the variable pointing to the publish directory.");

        Assert.True(
            Directory.Exists(TrimOutputDirectory),
            $"BLAZORCODEFIRST_TRIM_OUTPUT points to a directory that does not exist: {TrimOutputDirectory}");
    }
}

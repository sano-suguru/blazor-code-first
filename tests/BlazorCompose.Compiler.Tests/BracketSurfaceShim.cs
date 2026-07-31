using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Declares the bracket surface #87 will ship — element factories as properties, children through a
/// <c>params ReadOnlySpan&lt;View&gt;</c> indexer — in source, so the compiler paths that read it can be
/// verified before the runtime changes.
/// </summary>
/// <remarks>
/// <para>
/// The static class is named <c>Html</c> deliberately, and the shim compilation deliberately excludes
/// <c>BlazorCompose.Runtime</c>.  A second shipped static class cannot stage this migration: with
/// <c>using static BlazorCompose.Html;</c> and <c>using static BlazorCompose.Tags;</c> both in scope, the
/// method wins simple-name lookup and shadows the property, so <c>Div["a"]</c> is
/// <c>error CS0021: Cannot apply indexing with [] to an expression of type 'method group'</c>.  Only a class
/// actually named <c>Html</c> reproduces the final spelling: one <c>using static</c>, and an unqualified
/// <c>Div[…]</c> arriving as an <see cref="Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax"/>.
/// </para>
/// <para>
/// Fidelity to the shipped declarations is what makes verification against this shim meaningful, and it is
/// enforced by comparison against #95's committed baselines rather than by inspection: any divergence in
/// <c>View</c>, <c>ComposeComponentBase</c>, or <c>Decorations</c> that changes emitted text shows up as a
/// baseline mismatch.  Two properties are <em>not</em> covered that way and are recorded here instead:
/// <c>ElementBuilder</c> must be a <c>readonly struct</c> and not a <c>ref struct</c> (a <c>ref struct</c>
/// cannot be a generic type argument, so <c>.Param(c =&gt; c.Text, Div)</c> would be CS0306 and BC3014's
/// <c>ElementBuilder</c> arm would be unreachable), and whether the <em>shipped</em> declarations match at
/// all is only checked once #87 lands.
/// </para>
/// </remarks>
internal static class BracketSurfaceShim
{
    /// <summary>The path the shim tree is parsed under, so a diagnostic on it is identifiable.</summary>
    private const string ShimPath = "BracketSurface.Shim.cs";

    /// <summary>
    /// The bracket surface, declared exactly as #87 is expected to ship it.  Deliberately without XML
    /// documentation: the shipped declarations carry that, and repeating it here would invite the two
    /// copies to be maintained against each other rather than against the compiler.
    /// </summary>
    private const string Source = """
        namespace BlazorCompose;

        public readonly struct View
        {
            public static implicit operator View(string text) => default;

            public static implicit operator View(
                Microsoft.AspNetCore.Components.RenderFragment? fragment) => default;
        }

        public readonly struct ElementBuilder
        {
            public View this[params System.ReadOnlySpan<View> children] => default;

            public static implicit operator View(ElementBuilder builder) => default;
        }

        public readonly struct ComponentView<TComponent>
            where TComponent : Microsoft.AspNetCore.Components.IComponent
        {
            public ComponentView<TComponent> Param<TValue>(
                System.Func<TComponent, TValue> selector, TValue value) => this;

            public ComponentView<TComponent> Param(
                System.Func<TComponent, Microsoft.AspNetCore.Components.RenderFragment?> selector,
                View content) => this;

            public View this[params System.ReadOnlySpan<View> children] => default;

            public static implicit operator View(ComponentView<TComponent> view) => default;
        }

        public static class Html
        {
            public static ElementBuilder Div => default;
            public static ElementBuilder Span => default;
            public static ElementBuilder Button => default;
            public static ElementBuilder Nav => default;
            public static ElementBuilder Header => default;
            public static ElementBuilder Main => default;
            public static ElementBuilder Aside => default;
            public static ElementBuilder Footer => default;
            public static ElementBuilder Section => default;
            public static ElementBuilder Article => default;
            public static ElementBuilder P => default;
            public static ElementBuilder H1 => default;
            public static ElementBuilder H2 => default;
            public static ElementBuilder H3 => default;
            public static ElementBuilder H4 => default;
            public static ElementBuilder H5 => default;
            public static ElementBuilder H6 => default;
            public static ElementBuilder Ul => default;
            public static ElementBuilder Ol => default;
            public static ElementBuilder Li => default;
            public static ElementBuilder A => default;
            public static ElementBuilder Img => default;

            public static ElementBuilder Element(string tag) => default;

            public static View Fragment(params System.ReadOnlySpan<View> children) => default;

            public static View Raw(string rawHtml) => default;

            public static View If(
                bool condition, System.Func<View> then, System.Func<View>? otherwise = null) => default;

            public static View ForEach<T>(
                System.Collections.Generic.IEnumerable<T> source,
                System.Func<T, object?> key,
                System.Func<T, View> content) => default;

            public static ComponentView<TComponent> Component<TComponent>()
                where TComponent : Microsoft.AspNetCore.Components.IComponent => default;
        }

        public static class Decorations
        {
            public static ElementBuilder Class(this ElementBuilder element, string @class) => element;

            public static ElementBuilder Href(this ElementBuilder element, string value) => element;

            public static ElementBuilder Src(this ElementBuilder element, string value) => element;

            public static ElementBuilder Alt(this ElementBuilder element, string value) => element;

            public static ElementBuilder Id(this ElementBuilder element, string value) => element;

            public static ElementBuilder Type(this ElementBuilder element, string value) => element;

            public static ElementBuilder Title(this ElementBuilder element, string value) => element;

            public static ElementBuilder Role(this ElementBuilder element, string value) => element;

            public static ElementBuilder Attr(
                this ElementBuilder element, string name, string value) => element;

            public static ElementBuilder On(
                this ElementBuilder element, string eventName, System.Action handler) => element;

            public static ElementBuilder On(
                this ElementBuilder element,
                string eventName,
                System.Func<System.Threading.Tasks.Task> handler) => element;

            public static ElementBuilder OnClick(
                this ElementBuilder element, System.Action handler) => element;

            public static ElementBuilder OnClick(
                this ElementBuilder element,
                System.Func<System.Threading.Tasks.Task> handler) => element;
        }

        [System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
        public sealed class ComposableAttribute : System.Attribute;

        public abstract class ComposeComponentBase : Microsoft.AspNetCore.Components.ComponentBase
        {
            protected abstract View Body { get; }

            protected abstract void RenderView(
                Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder);

            protected sealed override void BuildRenderTree(
                Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) => RenderView(builder);
        }

        public abstract class ComposeLayoutBase : Microsoft.AspNetCore.Components.LayoutComponentBase
        {
            protected abstract View Chrome { get; }

            protected abstract void RenderView(
                Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder);

            protected sealed override void BuildRenderTree(
                Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) => RenderView(builder);
        }
        """;

    /// <summary>
    /// The C# error the input compilation is expected to carry: the generator is what supplies
    /// <c>RenderView</c>, so before it runs every component is missing an abstract member.
    /// </summary>
    private const string MissingRenderViewOverride = "CS0534";

    /// <summary>
    /// The conflict that appears when the runtime is referenced alongside the shim.  It must never appear:
    /// under it the shim's types win only by source shadowing, so nothing establishes which <c>Html</c> the
    /// compiler read, and every result measured against the shim would be measuring the runtime's surface
    /// as much as the shim's.
    /// </summary>
    private const string ImportedTypeConflict = "CS0436";

    /// <summary>The shim as a <c>(Path, Source)</c> tuple, for a compilation built directly.</summary>
    internal static (string Path, string Source) ShimFile => (ShimPath, Source);

    /// <summary>
    /// Compiles <paramref name="sources"/> against the shim and runs the generator, first asserting that
    /// the shim is the only <c>BlazorCompose</c> surface in the compilation.
    /// </summary>
    internal static GeneratorRunResult RunGenerator(params (string Path, string Source)[] sources)
    {
        var compilation = CompilationTestHost.CreateCompilationWithoutRuntime(
            [(ShimPath, Source), .. sources]);

        AssertShimIsTheOnlySurface(compilation);

        return CompilationTestHost.RunGenerator(compilation);
    }

    /// <summary>
    /// Asserts that the pre-generation compilation is healthy and unshadowed: no <c>CS0436</c>, and no error
    /// other than the <c>CS0534</c> the generator exists to resolve.  A shim that quietly stopped compiling
    /// would otherwise make every path under test unreachable while its tests reported the absence of a
    /// diagnostic as a pass.
    /// </summary>
    private static void AssertShimIsTheOnlySurface(Compilation compilation)
    {
        var diagnostics = compilation.GetDiagnostics();

        var conflicts = Describe(diagnostics.Where(
            static diagnostic => diagnostic.Id == ImportedTypeConflict));

        Assert.True(
            conflicts.Length == 0,
            $"The shim compilation also contains another BlazorCompose surface, so the shim's types win " +
            $"only by source shadowing and nothing establishes which declarations were analyzed: {conflicts}");

        var errors = Describe(diagnostics.Where(
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                && diagnostic.Id != MissingRenderViewOverride));

        Assert.True(errors.Length == 0, $"The shim compilation does not compile: {errors}");
    }

    private static string Describe(System.Collections.Generic.IEnumerable<Diagnostic> diagnostics) =>
        string.Join("; ", diagnostics.Select(static diagnostic => diagnostic.ToString()));

    /// <summary>Every error diagnostic the generator's output compilation reports, for assertion.</summary>
    internal static ImmutableArray<Diagnostic> OutputErrors(GeneratorRunResult result) =>
        [.. result.OutputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
}

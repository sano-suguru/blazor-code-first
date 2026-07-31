using System.Collections.Generic;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Runs #95's corpus a second time, written on the bracket surface, against the <em>same</em> committed
/// baselines.
/// </summary>
/// <remarks>
/// <para>
/// This is the verification that matters, and it is deliberately not a comparison of a method-form shim
/// against a bracket-form shim: two shims degrading identically stay green, which is the same blind spot the
/// relative-equality tests in <c>FactoryArgumentBindingTests</c> have.  Comparing against the baselines
/// instead proves #87's central claim — that re-spelling the call sites does not change the generated code —
/// before #87 is written, and it forces the shim's <c>View</c>, <c>ComposeComponentBase</c> and
/// <c>Decorations</c> declarations to match the shipped ones, because any divergence that reaches emitted
/// text surfaces here as a mismatch.
/// </para>
/// <para>
/// The case names are #95's, and no baseline is added: a case whose bracket form needed its own baseline
/// would be a case where the generated code <em>did</em> change, which is the thing being ruled out.  A
/// failure here means either the analyzer or the shim is wrong; <c>BracketSurfaceShim</c>'s compilation gate
/// is what distinguishes those.
/// </para>
/// </remarks>
public sealed class BracketSurfaceBaselineTests
{
    private const string CardSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Card : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter] public RenderFragment? Footer { get; set; }
        }
        """;

    /// <summary>Wraps <paramref name="body"/> in #95's canonical single-component host file.</summary>
    private static (string Path, string Source)[] Host(string body, string members = "") =>
    [
        ("Host.cs", $$"""
            using System.Collections.Generic;
            using BlazorCompose;
            using static BlazorCompose.Html;

            namespace T;

            public partial class Host : ComposeComponentBase
            {
                {{members}}
                protected override View Body => {{body}};
            }
            """),
    ];

    /// <summary>As <see cref="Host"/>, plus the <c>Card</c> component the component cases target.</summary>
    private static (string Path, string Source)[] HostWithCard(string body) =>
        [.. Host(body), ("Card.cs", CardSource)];

    public static TheoryData<string> CaseNames() => new([.. Cases.Keys]);

    private static readonly Dictionary<string, (string Path, string Source)[]> Cases =
        new(StringComparer.Ordinal)
        {
            // --- elements -----------------------------------------------------------------------
            // A childless element has no bracket form at all — `Img[]` is CS0443 — so it is the one shape
            // that becomes a bare property reference rather than an element access.
            ["element-childless"] = Host("""Img"""),
            ["element-single-text-child"] = Host("""H1["Title"]"""),
            ["element-several-children"] = Host("""Div[Span["a"], Span["b"], Span["c"]]"""),
            ["element-class"] = Host("""Div.Class("card")[Span["a"]]"""),
            ["element-class-folds-repeated"] = Host(
                """Div.Class("card").Attr("class", "wide").Class(_extra)[Span["a"]]""",
                """private string _extra = "x";"""),
            ["element-attribute-shortcuts"] = Host("""A.Href("/").Title("home")["Home"]"""),
            ["element-generic-attribute"] = Host("""Div.Attr("data-x", "1")[Span["a"]]"""),
            ["element-event-shortcut"] = Host(
                """Button.OnClick(() => _count++)["Increment"]""",
                """private int _count;"""),
            ["element-generic-event"] = Host(
                """Div.On("onmouseenter", () => _count++)[Span["a"]]""",
                """private int _count;"""),
            ["element-custom-tag"] = Host("""Element("custom-tag")["slotted"]"""),
            // An interpolated string, a bare string and a decorated element as siblings, each converting to
            // View through a different user-defined conversion inside one collection expression.
            ["element-nested-mixed-content"] = Host(
                """Div.Class("shell")[Span[$"Count: {_count}"], "bare text", Button.OnClick(() => _count++)["Go"]]""",
                """private int _count;"""),
            ["element-interpolated-attribute-value"] = Host(
                """Div.Attr("data-n", $"{_count}")[Span["a"]]""",
                """private int _count;"""),

            // --- Component<T>: the shapes that do not go through the component indexer -----------
            ["component-fragment-slot"] = HostWithCard(
                """Component<Card>().Param(c => c.Footer, Div["f"])"""),
            ["component-nested-in-element"] = HostWithCard(
                """Div.Class("shell")[Component<Card>().Param(c => c.Title, "t")]"""),

            // --- constructs nested inside an element: their surroundings change ------------------
            ["if-with-else-inside-element"] = Host(
                """Div.Class("shell")[If(_on, then: () => Span["Yes"], otherwise: () => Span["No"])]""",
                """private bool _on;"""),
            ["if-without-else-inside-element"] = Host(
                """Div[If(_on, then: () => Span["Yes"])]""",
                """private bool _on;"""),
            ["foreach-keyed-inside-element"] = Host(
                """Ul.Class("nav-list")[ForEach(_items, key: i => i, content: i => Li[i])]""",
                """private readonly List<string> _items = new();"""),
            ["foreach-nested-element-content"] = Host(
                """Ul[ForEach(_items, key: i => i, content: i => Li[A.Href($"/{i}")[i]])]""",
                """private readonly List<string> _items = new();"""),
            ["fragment-and-raw-inside-element"] = Host(
                """Div[Fragment(P["a"], P["b"]), Raw("<em>trusted</em>")]"""),

            // --- other emitter paths that must not shift -----------------------------------------
            ["composable-expansion"] = [("Host.cs", """
                using System.Collections.Generic;
                using BlazorCompose;
                using static BlazorCompose.Html;

                namespace T;

                public partial class Host : BlazorCompose.ComposeComponentBase
                {
                    private readonly List<string> _items = new();

                    protected override BlazorCompose.View Body => Panel("Heading", _items);

                    [BlazorCompose.Composable]
                    private static BlazorCompose.View Panel(string heading, List<string> items) =>
                        Div.Class("panel")[
                            H2[heading],
                            Ul[ForEach(items, key: i => i, content: i => Li[$"{heading}:{i}"])]
                        ];
                }
                """)],
            ["two-components-in-one-compilation"] = [
                ("First.cs", """
                    using BlazorCompose;
                    using static BlazorCompose.Html;

                    namespace T;

                    public partial class First : ComposeComponentBase
                    {
                        protected override View Body => Div.Class("a")[Span["first"]];
                    }
                    """),
                ("Second.cs", """
                    using BlazorCompose;
                    using static BlazorCompose.Html;

                    namespace T;

                    public partial class Second : ComposeComponentBase
                    {
                        protected override View Body => P["second"];
                    }
                    """),
            ],
            ["layout-chrome"] = [("Shell.cs", """
                using BlazorCompose;
                using static BlazorCompose.Html;

                namespace T;

                public partial class Shell : ComposeLayoutBase
                {
                    protected override View Chrome => Main.Class("shell")[Body];
                }
                """)],
        };

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void BracketFormGeneratedSource_MatchesTheSameBaseline(string caseName)
    {
        var result = BracketSurfaceShim.RunGenerator(Cases[caseName]);

        CompilationTestHost.AssertOutputCompiles(result);
        GeneratedSourceSnapshot.Verify(caseName, result);
    }
}

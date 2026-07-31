using System.Collections.Generic;
using System.Linq;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Pins the generator's complete emitted source for the design-time constructs whose spelling changes in
/// #87, and for the constructs that appear inside them.
/// </summary>
/// <remarks>
/// <para>
/// The case <em>names</em> are the stable identity here, not the input text.  #87 rewrites every input in
/// this file from <c>Div(children).Class("x")</c> to <c>Div.Class("x")[children]</c>; the baseline a case
/// points at must not change when that happens.  That is the whole safety net, so a case's input may be
/// re-spelled but a case must never be renamed or dropped to make a baseline agree.
/// </para>
/// <para>
/// Coverage is chosen for what the migration can break, not for what the emitter can emit: every element
/// shape (the constructs that gain brackets), every <c>Component&lt;T&gt;</c> shape (likewise), and the
/// region-opening and frame-less constructs — <c>If</c>, <c>ForEach</c>, <c>Fragment</c>, <c>Raw</c> —
/// nested inside an element, which is the position whose surrounding syntax changes around them.
/// </para>
/// </remarks>
public sealed class SnapshotCorpusTests
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

    /// <summary>Wraps <paramref name="body"/> in the canonical single-component host file.</summary>
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
            // --- elements: the constructs that gain brackets ------------------------------------
            // Void element, no children and no decoration: the case #87 turns into a bare property
            // reference, and the one whose spelling changes most.
            ["element-childless"] = Host("""Img()"""),
            ["element-single-text-child"] = Host("""H1("Title")"""),
            ["element-several-children"] = Host("""Div(Span("a"), Span("b"), Span("c"))"""),
            ["element-class"] = Host("""Div(Span("a")).Class("card")"""),
            ["element-class-folds-repeated"] = Host(
                """Div(Span("a")).Class("card").Attr("class", "wide").Class(_extra)""",
                """private string _extra = "x";"""),
            ["element-attribute-shortcuts"] = Host("""A("Home").Href("/").Title("home")"""),
            ["element-generic-attribute"] = Host("""Div(Span("a")).Attr("data-x", "1")"""),
            ["element-event-shortcut"] = Host(
                """Button("Increment").OnClick(() => _count++)""",
                """private int _count;"""),
            ["element-generic-event"] = Host(
                """Div(Span("a")).On("onmouseenter", () => _count++)""",
                """private int _count;"""),
            ["element-custom-tag"] = Host("""Element("custom-tag", "slotted")"""),
            ["element-nested-mixed-content"] = Host(
                """Div(Span($"Count: {_count}"), "bare text", Button("Go").OnClick(() => _count++)).Class("shell")""",
                """private int _count;"""),
            ["element-interpolated-attribute-value"] = Host(
                """Div(Span("a")).Attr("data-n", $"{_count}")""",
                """private int _count;"""),

            // --- Component<T>: also gains brackets ----------------------------------------------
            ["component-no-parameters"] = HostWithCard("""Component<Card>()"""),
            ["component-scalar-parameter"] = HostWithCard("""Component<Card>().Param(c => c.Title, "t")"""),
            ["component-child-content"] = HostWithCard("""Component<Card>(Div("x"))"""),
            ["component-child-content-and-parameter"] = HostWithCard(
                """Component<Card>(Div("x")).Param(c => c.Title, "t")"""),
            ["component-fragment-slot"] = HostWithCard(
                """Component<Card>().Param(c => c.Footer, Div("f"))"""),
            ["component-nested-in-element"] = HostWithCard(
                """Div(Component<Card>().Param(c => c.Title, "t")).Class("shell")"""),

            // --- constructs nested inside an element: their surroundings change -----------------
            ["if-with-else-inside-element"] = Host(
                """Div(If(_on, then: () => Span("Yes"), otherwise: () => Span("No"))).Class("shell")""",
                """private bool _on;"""),
            ["if-without-else-inside-element"] = Host(
                """Div(If(_on, then: () => Span("Yes")))""",
                """private bool _on;"""),
            ["foreach-keyed-inside-element"] = Host(
                """Ul(ForEach(_items, key: i => i, content: i => Li(i))).Class("nav-list")""",
                """private readonly List<string> _items = new();"""),
            ["foreach-nested-element-content"] = Host(
                """Ul(ForEach(_items, key: i => i, content: i => Li(A(i).Href($"/{i}"))))""",
                """private readonly List<string> _items = new();"""),
            ["fragment-and-raw-inside-element"] = Host(
                """Div(Fragment(P("a"), P("b")), Raw("<em>trusted</em>"))"""),

            // --- other emitter paths that must not shift ----------------------------------------
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
                        Div(
                            H2(heading),
                            Ul(ForEach(items, key: i => i, content: i => Li($"{heading}:{i}")))
                        ).Class("panel");
                }
                """)],
            // Two components in one compilation: the only corpus case that emits more than one file, so
            // it is what exercises the per-file separator and the ordering-by-hint-name in the harness.
            ["two-components-in-one-compilation"] = [
                ("First.cs", """
                    using BlazorCompose;
                    using static BlazorCompose.Html;

                    namespace T;

                    public partial class First : ComposeComponentBase
                    {
                        protected override View Body => Div(Span("first")).Class("a");
                    }
                    """),
                ("Second.cs", """
                    using BlazorCompose;
                    using static BlazorCompose.Html;

                    namespace T;

                    public partial class Second : ComposeComponentBase
                    {
                        protected override View Body => P("second");
                    }
                    """),
            ],
            ["layout-chrome"] = [("Shell.cs", """
                using BlazorCompose;
                using static BlazorCompose.Html;

                namespace T;

                public partial class Shell : ComposeLayoutBase
                {
                    protected override View Chrome => Main(Body).Class("shell");
                }
                """)],
        };

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void GeneratedSource_MatchesBaseline(string caseName)
    {
        var result = CompilationTestHost.RunGenerator(Cases[caseName]);

        CompilationTestHost.AssertOutputCompiles(result);
        GeneratedSourceSnapshot.Verify(caseName, result);
    }

    [Fact]
    public void Baselines_CorrespondOneToOneWithCases()
    {
        // A baseline with no case is not merely untidy: it exists, looks like coverage in a diff, and is
        // never compared against anything. A case with no baseline fails its own theory row, but is
        // reported here too so both directions are readable from one failure.
        var onDisk = GeneratedSourceSnapshot.EnumerateBaselineCaseNames().ToList();
        var declared = Cases.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToList();

        Assert.Equal(declared, onDisk);
    }
}

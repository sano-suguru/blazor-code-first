using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ComponentSlotGeneratorTests
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

    [Fact]
    public void ComponentWithChildren_CompilesWithoutCSharpErrors()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Card>().Param(c => c.Title, "t")[Div["x"]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));

        // The new overloads must exist on the runtime surface: any CS error here means the API is missing.
        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
            d => d.Id.StartsWith("CS", System.StringComparison.Ordinal));
    }

    [Fact]
    public void FragmentParamOverload_CompilesWithoutCSharpErrors()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Card>().Param(c => c.Footer, Div["f"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));

        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
            d => d.Id.StartsWith("CS", System.StringComparison.Ordinal));
    }

    private static string GeneratedHost(GeneratorRunResult result) =>
        result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();

    [Fact]
    public void ComponentWithChildren_BindsChildContentSlot()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Card>().Param(c => c.Title, "t")[Div["x"]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("__builder.OpenComponent<global::T.Card>(0);", code);
        Assert.Contains("__builder.AddComponentParameter(1, \"Title\", \"t\");", code);
        Assert.Contains(
            "__builder.AddComponentParameter(2, \"ChildContent\", "
                + "(global::Microsoft.AspNetCore.Components.RenderFragment)((__builder) =>",
            code);
        // The slot's content is fully static, so it folds into one markup frame. What this test needs from
        // it is unchanged: the content starts at the sequence number after the slot parameter.
        Assert.Contains("__builder.AddMarkupContent(3, \"<div>x</div>\");", code);
    }

    [Fact]
    public void ComponentWithMultipleChildren_PutsThemAllInOneSlot()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Card>()[Div["a"], "text"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        // Both children share the single ChildContent fragment; only one AddComponentParameter for it.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(code, @"""ChildContent"""));
        // Both children are static and adjacent, so they fold into one markup frame — which shows them
        // sharing the one slot more directly than the pair of frame assertions this replaced.
        Assert.Contains("__builder.AddMarkupContent(2, \"<div>a</div>text\");", code);
    }

    [Fact]
    public void FragmentParam_BindsNamedSlot()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Card>().Param(c => c.Footer, Div["f"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(
            "__builder.AddComponentParameter(1, \"Footer\", "
                + "(global::Microsoft.AspNetCore.Components.RenderFragment)((__builder) =>",
            code);
        Assert.Contains("__builder.AddMarkupContent(2, \"<div>f</div>\");", code);
    }

    [Fact]
    public void FragmentParam_ChildContentByName_IsAlsoAccepted()
    {
        // Razor permits the equivalent attribute form, so this spelling is legal, just verbose.
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Card>().Param(c => c.ChildContent, Div["x"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("\"ChildContent\"", GeneratedHost(result));
    }

    [Fact]
    public void ComponentWithChildren_NestedSlots_ContinueTheFlatSequence()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Card>()[Component<Card>()[Div["deep"]]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // outer OpenComponent=0, outer slot=1, inner OpenComponent=2, inner slot=3, folded content=4. The
        // claim under test is that a slot does not restart its own sequence space, and the inner
        // component's 2 and the inner slot's content at 4 still show that.
        Assert.Contains("__builder.OpenComponent<global::T.Card>(0);", code);
        Assert.Contains("__builder.OpenComponent<global::T.Card>(2);", code);
        Assert.Contains("__builder.AddMarkupContent(4, \"<div>deep</div>\");", code);
    }

    [Fact]
    public void ComponentWithChildren_RealRenderFragmentValue_StillUsesTheScalarChannel()
    {
        // A genuine RenderFragment binds through the generic Param and is emitted verbatim, not wrapped
        // in a lambda.
        const string host = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                [Parameter] public RenderFragment? Incoming { get; set; }
                protected override View Body => Component<Card>().Param(c => c.ChildContent, Incoming);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("__builder.AddComponentParameter(1, \"ChildContent\", Incoming);", code);
        Assert.DoesNotContain("RenderFragment)((__builder)", code);
    }

    [Fact]
    public void ComponentWithChildren_ExplicitCollectionArgument_IsRejected()
    {
        // One whole collection passed to the params parameter is not a child list; mirrors Div[children: arr].
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private static readonly View[] _kids = [];
                protected override View Body => Component<Card>()[children: _kids];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF1003");
    }

    private const string InheritedChildContentSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class BaseCard : ComponentBase
        {
            [Parameter] public RenderFragment? ChildContent { get; set; }
        }
        public class DerivedCard : BaseCard
        {
        }
        """;

    [Fact]
    public void ComponentWithChildren_ChildContentInheritedFromBaseClass_IsAccepted()
    {
        // Regression guard: HasUsableChildContent must walk the base-type chain (Roslyn's GetMembers on
        // the derived type alone would not see a ChildContent property declared only on the base class).
        // Without that walk, a component that inherits ChildContent instead of redeclaring it would be
        // rejected with a false BCF3013 even though Blazor itself accepts it at runtime.
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<DerivedCard>()[Div["x"]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("DerivedCard.cs", InheritedChildContentSource), ("Host.cs", host));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3013");
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("\"ChildContent\"", GeneratedHost(result));
    }

    private const string GridSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Grid<TItem> : ComponentBase
        {
            [Parameter] public RenderFragment? ChildContent { get; set; }
        }
        """;

    [Fact]
    public void ComponentWithChildren_GenericComponent_EmitsFullyQualifiedOpenComponent()
    {
        // Guards against the generator losing or mis-qualifying the type argument on OpenComponent<T>
        // when the component is both generic and receives slotted children.
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => Component<Grid<int>>()[Div["g"]];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Grid.cs", GridSource), ("Host.cs", host));

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("__builder.OpenComponent<global::T.Grid<int>>(0);", GeneratedHost(result));
    }

    [Fact]
    public void ComponentWithChildren_KeyedForEachInsideSlot_EmitsRegionAndSetKeyInsideTheLambda()
    {
        // Guards against a keyed ForEach nested inside a slot's fragment losing its OpenRegion/SetKey
        // wrapping, or that wrapping being emitted outside the fragment lambda where it would run
        // against the host's own builder instead of the slot's.
        const string host = """
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private readonly List<int> _items = new();
                protected override View Body =>
                    Component<Card>()[ForEach(_items, key: i => i, content: i => Li[i.ToString()])];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        int lambdaIdx = code.IndexOf(
            "AddComponentParameter(1, \"ChildContent\",", System.StringComparison.Ordinal);
        int regionIdx = code.IndexOf("OpenRegion(", System.StringComparison.Ordinal);
        int foreachIdx = code.IndexOf("foreach (", System.StringComparison.Ordinal);
        int liIdx = code.IndexOf("OpenElement(", System.StringComparison.Ordinal);
        int keyIdx = code.IndexOf("SetKey(", System.StringComparison.Ordinal);

        Assert.True(lambdaIdx >= 0, "the slot's ChildContent parameter must be emitted");
        Assert.True(lambdaIdx < regionIdx, "the ForEach region must be emitted inside the fragment lambda");
        Assert.True(regionIdx < foreachIdx, "OpenRegion must precede the transplanted foreach loop");
        Assert.True(foreachIdx < liIdx, "the foreach loop must precede the li content root's OpenElement");
        Assert.True(liIdx < keyIdx, "SetKey must follow the content root's OpenElement");
    }

    [Fact]
    public void ForEach_WithSlotContent_EmitsSetKeyBeforeTheSlotParameter()
    {
        // Regression guard mirroring the Task-9 SetKey defect, but for a slot instead of a plain
        // element: SetKey applies to whichever frame is currently open, so it must be emitted right
        // after OpenComponent and before the slot's AddComponentParameter opens the fragment. Emitting
        // it after the slot parameter would try to key a frame that is no longer the component frame
        // and throw at render time.
        const string host = """
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private readonly List<int> _items = new();
                protected override View Body =>
                    ForEach(_items, key: i => i, content: i => Component<Card>()[Span[i.ToString()]]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        int openIdx = code.IndexOf("OpenComponent<global::T.Card>", System.StringComparison.Ordinal);
        int keyIdx = code.IndexOf("SetKey(", System.StringComparison.Ordinal);
        int paramIdx = code.IndexOf("AddComponentParameter(", System.StringComparison.Ordinal);

        Assert.True(openIdx >= 0, "the component should be opened");
        Assert.True(openIdx < keyIdx, "SetKey must be emitted after OpenComponent");
        Assert.True(keyIdx < paramIdx, "SetKey must be emitted before the slot's AddComponentParameter");
    }

    [Fact]
    public void ComponentWithChildren_ComposableCallInsideSlot_ExpandsInsideTheLambda()
    {
        // Guards against a [Composable] call nested inside a slot failing to expand statically (falling
        // back to an opaque runtime call). "new" and the .Class("badge") decoration are both constant, so
        // the lone-hole substitution rule (#140) carries the constant through and the whole call folds
        // into one markup frame; there is no argument local left to hoist out of the fragment lambda, so
        // this case instead pins that the folded frame itself lands inside the lambda.
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                [Composable]
                private static View Badge(string label) => Span.Class("badge")[label];

                protected override View Body => Component<Card>()[Badge("new")];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // The call must be statically expanded, not left as a runtime method invocation.
        Assert.DoesNotContain("Badge(", code);
        Assert.DoesNotContain("__bcf_arg_", code);

        int lambdaIdx = code.IndexOf(
            "AddComponentParameter(1, \"ChildContent\",", System.StringComparison.Ordinal);
        int markupIdx = code.IndexOf(
            """<span class=\"badge\">new</span>""", System.StringComparison.Ordinal);

        Assert.True(lambdaIdx >= 0, "the slot's ChildContent parameter must be emitted");
        Assert.True(
            lambdaIdx < markupIdx,
            "the composable's folded markup must be emitted inside the fragment lambda");
    }

    [Fact]
    public void ComponentWithChildren_ComposableCallInsideSlotWithNonConstantArgument_DeclaresLocalInsideTheLambda()
    {
        // The companion to the case above for when the call does not fold: a non-constant argument keeps
        // the expansion local live, and it must be declared inside the fragment lambda, where it would
        // otherwise be out of scope for the generated fragment delegate if hoisted above it.
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private string _label => "new";

                [Composable]
                private static View Badge(string label) => Span[label];

                protected override View Body => Component<Card>()[Badge(_label)];
            }
            """;

        var result = CompilationTestHost.RunGenerator(("Card.cs", CardSource), ("Host.cs", host));
        var code = GeneratedHost(result);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // The call must be statically expanded, not left as a runtime method invocation.
        Assert.DoesNotContain("Badge(", code);

        int lambdaIdx = code.IndexOf(
            "AddComponentParameter(1, \"ChildContent\",", System.StringComparison.Ordinal);
        int localIdx = code.IndexOf("__bcf_arg_", System.StringComparison.Ordinal);

        Assert.True(lambdaIdx >= 0, "the slot's ChildContent parameter must be emitted");
        Assert.True(
            lambdaIdx < localIdx,
            "the composable's argument local must be declared inside the fragment lambda");
    }
}

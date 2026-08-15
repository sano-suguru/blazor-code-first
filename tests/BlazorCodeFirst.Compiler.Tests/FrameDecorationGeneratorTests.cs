namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The non-attribute frame decorations of <c>ARCHITECTURE.md</c> §2.7(E): <c>.Key</c> on an element and
/// on a component, and <c>.RenderMode</c> on a component. What each case fixes is the pair the section
/// names — where the call lands relative to the attribute frames, and whether it consumes a sequence
/// number — because those two are what a decoration outside the attribute fold can get wrong.
/// </summary>
public sealed class FrameDecorationGeneratorTests
{
    private const string KeyOnDivSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _cls => "tab";
            private object _id => 7;
            protected override View Body => Html.Div.Class(_cls).Key(_id)[Html.Span["x"]];
        }
        """;

    private const string KeyWrittenNullSource = """
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            private string _cls => "tab";
            protected override View Body => Html.Div.Class(_cls).Key(null)[Html.Span["x"]];
        }
        """;

    [Fact]
    public void Key_OnElement_EmitsSetKeyRightAfterOpenAndConsumesNoSequence()
    {
        var result = CompilationTestHost.RunGenerator(KeyOnDivSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.SetKey(_id)", generated);

        // The class attribute keeps seq 1: SetKey took no number on the way past.
        Assert.Contains("__builder.AddAttribute(1, \"class\", _cls)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Key_WrittenNull_EmitsNoSetKey()
    {
        var result = CompilationTestHost.RunGenerator(KeyWrittenNullSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.DoesNotContain("SetKey", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Key_OnAnOtherwiseConstantElement_StopsTheFold()
    {
        var keyed = CompilationTestHost.RunGenerator(Body("""Html.Div.Key(1)[Html.Span["x"]]"""));
        var generated = Assert.Single(keyed.GeneratedSources).SourceText.ToString();

        // Every value here is constant, so without the key this whole subtree would collapse into one
        // AddMarkupContent frame. The key has no markup spelling, so the element path has to stay.
        Assert.Contains("__builder.OpenElement(0, \"div\")", generated);
        Assert.Contains("__builder.SetKey(1)", generated);
        CompilationTestHost.AssertOutputCompiles(keyed);

        var unkeyed = CompilationTestHost.RunGenerator(Body("""Html.Div[Html.Span["x"]]"""));
        Assert.Contains(
            "__builder.AddMarkupContent(0,",
            Assert.Single(unkeyed.GeneratedSources).SourceText.ToString());
    }

    [Fact]
    public void SecondKey_ReportsBCF3033()
    {
        var diagnostics = CompilationTestHost
            .RunGenerator(Body("""Html.Div.Key(1).Key(2)["x"]"""))
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3033");
    }

    [Fact]
    public void KeyDeclinedAfterAKey_IsStillTheDuplicate()
    {
        // Writing null declines a key; it does not retract one. The pair is BCF3033, not a div that
        // quietly ends up unkeyed.
        var diagnostics = CompilationTestHost
            .RunGenerator(Body("""Html.Div.Key(1).Key(null)["x"]"""))
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3033");
    }

    [Fact]
    public void Key_OnComponent_EmitsSetKeyBeforeTheParameters()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;

            public class Row : ComponentBase
            {
                [Parameter] public string? Label { get; set; }
            }

            public partial class C : BodyComponentBase
            {
                private int _id = 3;
                protected override View Body =>
                    Html.Component<Row>().Key(_id).Param(c => c.Label, "x");
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenComponent<global::Row>(0)", generated);
        Assert.Contains("__builder.SetKey(_id)", generated);

        // The parameter keeps seq 1: SetKey took no number here either.
        Assert.Contains("__builder.AddComponentParameter(1, \"Label\", \"x\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Key_WrittenAfterAParameter_ReachesTheSameComponent()
    {
        // .Key selects no parameter, so it has to survive the chain in either order.
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;

            public class Row : ComponentBase
            {
                [Parameter] public string? Label { get; set; }
            }

            public partial class C : BodyComponentBase
            {
                private int _id = 3;
                protected override View Body =>
                    Html.Component<Row>().Param(c => c.Label, "x").Key(_id);
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.SetKey(_id)", generated);
        Assert.Contains("__builder.AddComponentParameter(1, \"Label\", \"x\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void KeyOnAKeyedForEachContentRoot_ReportsBCF3032()
    {
        var diagnostics = CompilationTestHost.RunGenerator("""
            using System.Collections.Generic;
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private readonly List<string> _items = ["a"];
                protected override View Body =>
                    Html.ForEach(_items, i => i, i => Html.Div.Key(i)[i]);
            }
            """).Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3032");
    }

    [Fact]
    public void KeyOnADeclinedForEachContentRoot_IsAccepted()
    {
        // key: null attaches nothing, so the root's own key is the only one and there is no collision.
        // The rule BCF3003 follows for the same reason (#172), on the other half of the same walk.
        var result = CompilationTestHost.RunGenerator("""
            using System.Collections.Generic;
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private readonly List<string> _items = ["a"];
                protected override View Body =>
                    Html.ForEach(_items, null, i => Html.Div.Key(i)[i]);
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id is "BCF3032" or "BCF3003");
        Assert.Contains("__builder.SetKey(", Assert.Single(result.GeneratedSources).SourceText.ToString());
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A component with a scalar parameter and a <c>ChildContent</c> slot, so the render mode has both
    /// kinds of parameter frame to land after. Declares no render mode of its own, which is the case
    /// BCF3034 leaves open.
    /// </summary>
    private const string RenderModeHostSource = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;
        using Microsoft.AspNetCore.Components.Web;

        public class Panel : ComponentBase
        {
            [Parameter] public string? Label { get; set; }
            [Parameter] public RenderFragment? ChildContent { get; set; }
        }

        public partial class C : BodyComponentBase
        {
            protected override View Body =>
                Html.Component<Panel>()
                    .Param(c => c.Label, "x")
                    .RenderMode(RenderMode.InteractiveServer)[Html.Span["body"]];
        }
        """;

    [Fact]
    public void RenderMode_IsEmittedAfterEveryParameterFrameAndConsumesNoSequence()
    {
        var result = CompilationTestHost.RunGenerator(RenderModeHostSource);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        var renderModeAt = generated.IndexOf("AddComponentRenderMode", System.StringComparison.Ordinal);
        var slotAt = generated.IndexOf("\"ChildContent\"", System.StringComparison.Ordinal);
        Assert.True(slotAt >= 0 && renderModeAt > slotAt, "the render mode must follow the slot parameter");

        // The slot's content keeps numbering from the flat counter; the render mode took nothing.
        Assert.Contains("__builder.AddComponentParameter(1, \"Label\", \"x\")", generated);
        Assert.Contains(
            "__builder.AddComponentRenderMode(global::Microsoft.AspNetCore.Components.Web.RenderMode.InteractiveServer)",
            generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void RenderMode_OnAComponentWhoseDeclarationFixesIt_ReportsBCF3034()
    {
        var diagnostics = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Web;

            // The framework ships no concrete RenderModeAttribute: it is abstract, and Razor's
            // `@rendermode` directive generates a subclass. A C#-authored component declares its own.
            public sealed class InteractiveAttribute : RenderModeAttribute
            {
                public override IComponentRenderMode Mode => RenderMode.InteractiveServer;
            }

            [Interactive]
            public class Fixed : ComponentBase { }

            public partial class C : BodyComponentBase
            {
                protected override View Body =>
                    Html.Component<Fixed>().RenderMode(RenderMode.InteractiveServer);
            }
            """).Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3034");
    }

    [Fact]
    public void RenderMode_OnAComponentInheritingAFixedMode_ReportsBCF3034()
    {
        // The framework reads the attribute up the base chain, so stopping at the derived type would let
        // this shape through to the runtime throw the diagnostic replaces.
        var diagnostics = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Web;

            // The framework ships no concrete RenderModeAttribute: it is abstract, and Razor's
            // `@rendermode` directive generates a subclass. A C#-authored component declares its own.
            public sealed class InteractiveAttribute : RenderModeAttribute
            {
                public override IComponentRenderMode Mode => RenderMode.InteractiveServer;
            }

            [Interactive]
            public class FixedBase : ComponentBase { }
            public class Derived : FixedBase { }

            public partial class C : BodyComponentBase
            {
                protected override View Body =>
                    Html.Component<Derived>().RenderMode(RenderMode.InteractiveServer);
            }
            """).Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3034");
    }

    [Fact]
    public void SecondRenderMode_ReportsBCF3033()
    {
        var diagnostics = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using Microsoft.AspNetCore.Components.Web;

            public class Panel : ComponentBase { }

            public partial class C : BodyComponentBase
            {
                protected override View Body =>
                    Html.Component<Panel>()
                        .RenderMode(RenderMode.InteractiveServer)
                        .RenderMode(RenderMode.InteractiveAuto);
            }
            """).Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3033");
    }

    [Fact]
    public void Ref_OnElement_IsCapturedAfterTheAttributesAndConsumesASequenceNumber()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;

            public partial class C : BodyComponentBase
            {
                private ElementReference _input;
                private string _cls => "field";
                protected override View Body =>
                    Html.Input.Class(_cls).Ref(r => _input = r);
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"input\")", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", _cls)", generated);

        // Unlike SetKey this one takes a number, and it takes the one after the attributes.
        Assert.Contains("__builder.AddElementReferenceCapture(2, r => _input = r)", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Ref_OnElement_ComesBeforeTheChildren()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;

            public partial class C : BodyComponentBase
            {
                private ElementReference _box;
                private string _text => "x";

                // A dynamic child, so it stays a frame of its own rather than folding into markup: what
                // this fixes is the order of two frames, which a fold would remove one of.
                protected override View Body => Html.Div.Ref(r => _box = r)[Html.Span[_text]];
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        var captureAt = generated.IndexOf("AddElementReferenceCapture(1", System.StringComparison.Ordinal);
        var childAt = generated.IndexOf("OpenElement(2", System.StringComparison.Ordinal);
        Assert.True(captureAt >= 0 && childAt > captureAt, "the capture must precede the children");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void Ref_OnComponent_CastsTheInstanceForTheAuthorsAction()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;

            public class Row : ComponentBase
            {
                [Parameter] public string? Label { get; set; }
            }

            public partial class C : BodyComponentBase
            {
                private Row? _row;
                protected override View Body =>
                    Html.Component<Row>().Param(c => c.Label, "x").Ref(c => _row = c);
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.AddComponentParameter(1, \"Label\", \"x\")", generated);
        Assert.Contains(
            "__builder.AddComponentReferenceCapture(2, __value => "
                + "((global::System.Action<global::Row>)(c => _row = c))((global::Row)__value));",
            generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void SecondRef_ReportsBCF3033()
    {
        var diagnostics = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;

            public partial class C : BodyComponentBase
            {
                private ElementReference _a;
                private ElementReference _b;
                protected override View Body =>
                    Html.Div.Ref(r => _a = r).Ref(r => _b = r);
            }
            """).Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3033");
    }

    private static string Body(string body) => $$"""
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => {{body}};
        }
        """;
}

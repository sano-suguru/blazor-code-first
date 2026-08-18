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
        Assert.Contains("__builder.AddComponentParameter(1, \"Label\", (global::System.String?)(\"x\"))", generated);
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
        Assert.Contains("__builder.AddComponentParameter(1, \"Label\", (global::System.String?)(\"x\"))", generated);
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
        Assert.Contains("__builder.AddComponentParameter(1, \"Label\", (global::System.String?)(\"x\"))", generated);
        Assert.Contains(
            "__builder.AddComponentRenderMode(global::Microsoft.AspNetCore.Components.Web.RenderMode.InteractiveServer)",
            generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void RenderMode_OnAComponentWhoseDeclarationFixesIt_ReportsBCF3034()
    {
        var diagnostics = CompilationTestHost.RunGenerator(FixedModeHost("""
            [Interactive]
            public class Fixed : ComponentBase { }
            """, "Fixed")).Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3034");
    }

    [Fact]
    public void RenderMode_OnAComponentInheritingAFixedMode_ReportsBCF3034()
    {
        // The framework reads the attribute up the base chain, so stopping at the derived type would let
        // this shape through to the runtime throw the diagnostic replaces.
        var diagnostics = CompilationTestHost.RunGenerator(FixedModeHost("""
            [Interactive]
            public class FixedBase : ComponentBase { }
            public class Derived : FixedBase { }
            """, "Derived")).Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3034");
    }

    /// <summary>
    /// A component source declaring the render-mode attribute an author has to write, plus
    /// <paramref name="declarations"/>, and a host writing <c>.RenderMode</c> on
    /// <paramref name="componentName"/>.
    /// </summary>
    /// <remarks>
    /// The attribute is declared here because the framework ships none: <c>RenderModeAttribute</c> is
    /// abstract, and Razor's <c>@rendermode</c> directive generates a subclass per component. Shared by the
    /// two BCF3034 cases so the explanation and the shim cannot drift apart.
    /// </remarks>
    private static string FixedModeHost(string declarations, string componentName) => $$"""
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;
        using Microsoft.AspNetCore.Components.Web;

        public sealed class InteractiveAttribute : RenderModeAttribute
        {
            public override IComponentRenderMode Mode => RenderMode.InteractiveServer;
        }

        {{declarations}}

        public partial class C : BodyComponentBase
        {
            protected override View Body =>
                Html.Component<{{componentName}}>().RenderMode(RenderMode.InteractiveServer);
        }
        """;

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

        Assert.Contains("__builder.AddComponentParameter(1, \"Label\", (global::System.String?)(\"x\"))", generated);
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

    [Fact]
    public void FormName_OnElement_IsEmittedAfterTheAttributesAndConsumesNoSequence()
    {
        var result = CompilationTestHost.RunGenerator(Body(
            """Html.Form.Class("f").FormName("save")["x"]"""));
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.OpenElement(0, \"form\")", generated);
        Assert.Contains("__builder.AddAttribute(1, \"class\", \"f\")", generated);
        Assert.Contains("__builder.AddNamedEvent(\"onsubmit\", \"save\")", generated);

        // Unlike AddElementReferenceCapture this one takes no number: the child keeps seq 2, not 3.
        Assert.Contains("__builder.AddContent(2, \"x\")", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void FormName_OnElement_ComesBeforeTheChildren()
    {
        // A dynamic child, so it stays a frame of its own rather than folding into markup: what this
        // fixes is the order of two frames, which a fold would remove one of (mirrors
        // Ref_OnElement_ComesBeforeTheChildren above).
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private string _text => "x";
                protected override View Body => Html.Form.FormName("save")[Html.Span[_text]];
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        var namedEventAt = generated.IndexOf("AddNamedEvent(", System.StringComparison.Ordinal);
        var childAt = generated.IndexOf("OpenElement(1", System.StringComparison.Ordinal);
        Assert.True(namedEventAt >= 0 && childAt > namedEventAt, "the named event must precede the children");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void FormName_AndRef_TogetherEmitFormNameFirst()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;

            public partial class C : BodyComponentBase
            {
                private ElementReference _form;
                protected override View Body =>
                    Html.Form.FormName("save").Ref(r => _form = r)["x"];
            }
            """);
        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        var namedEventAt = generated.IndexOf("AddNamedEvent(", System.StringComparison.Ordinal);
        var captureAt = generated.IndexOf("AddElementReferenceCapture(", System.StringComparison.Ordinal);
        Assert.True(namedEventAt >= 0 && captureAt > namedEventAt, "FormName must come before Ref");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void FormName_OnAnOtherwiseConstantElement_StopsTheFold()
    {
        var named = CompilationTestHost.RunGenerator(Body(
            """Html.Form.FormName("save")[Html.Span["x"]]"""));
        var generated = Assert.Single(named.GeneratedSources).SourceText.ToString();

        // Every value here is constant, so without FormName this whole subtree would collapse into one
        // AddMarkupContent frame. AddNamedEvent has no markup spelling, so the element path has to stay.
        Assert.Contains("__builder.OpenElement(0, \"form\")", generated);
        Assert.Contains("__builder.AddNamedEvent(\"onsubmit\", \"save\")", generated);
        CompilationTestHost.AssertOutputCompiles(named);

        var unnamed = CompilationTestHost.RunGenerator(Body("""Html.Form[Html.Span["x"]]"""));
        Assert.Contains(
            "__builder.AddMarkupContent(0,",
            Assert.Single(unnamed.GeneratedSources).SourceText.ToString());
    }

    [Fact]
    public void SecondFormName_ReportsBCF3033()
    {
        var diagnostics = CompilationTestHost
            .RunGenerator(Body("""Html.Form.FormName("a").FormName("b")["x"]"""))
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3033");
    }

    [Fact]
    public void FormName_WrittenAsLiteralNull_ReportsBCF3039()
    {
        var diagnostics = CompilationTestHost
            .RunGenerator(Body("""Html.Form.FormName(null!)["x"]"""))
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3039");
    }

    [Fact]
    public void FormName_WrittenAsEmptyStringLiteral_ReportsBCF3039()
    {
        var diagnostics = CompilationTestHost
            .RunGenerator(Body("""Html.Form.FormName("")["x"]"""))
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3039");
    }

    [Fact]
    public void FormName_WrittenAsNonConstantExpression_IsAccepted()
    {
        var result = CompilationTestHost.RunGenerator("""
            using BlazorCodeFirst;

            public partial class C : BodyComponentBase
            {
                private string _name = "save";
                protected override View Body => Html.Form.FormName(_name)["x"];
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3039");
        Assert.Contains(
            "__builder.AddNamedEvent(\"onsubmit\", _name)",
            Assert.Single(result.GeneratedSources).SourceText.ToString());
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void FormName_OnANonFormElement_ReportsBCF3040()
    {
        var diagnostics = CompilationTestHost
            .RunGenerator(Body("""Html.Div.FormName("save")["x"]"""))
            .Diagnostics;

        Assert.Contains(diagnostics, d => d.Id == "BCF3040");
    }

    [Fact]
    public void FormName_OnAFormElement_DoesNotReportBCF3040()
    {
        var result = CompilationTestHost.RunGenerator(Body(
            """Html.Form.FormName("save")["x"]"""));

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3040");
        CompilationTestHost.AssertOutputCompiles(result);
    }

    private static string Body(string body) => $$"""
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => {{body}};
        }
        """;
}

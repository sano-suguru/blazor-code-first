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

    private static string Body(string body) => $$"""
        using BlazorCodeFirst;

        public partial class C : BodyComponentBase
        {
            protected override View Body => {{body}};
        }
        """;
}

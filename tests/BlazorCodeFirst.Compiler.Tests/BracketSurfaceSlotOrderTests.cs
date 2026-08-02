namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Pins the order of a component's <c>Slots</c> channel on the bracket surface, which no baseline covers:
/// the corpus has no case with both a fragment-slot <c>.Param</c> and bracketed children.
/// </summary>
public sealed class BracketSurfaceSlotOrderTests
{
    /// <summary>A component with two RenderFragment parameters and nothing else the test needs.</summary>
    private const string PanelSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Panel : ComponentBase
        {
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter] public RenderFragment? Footer { get; set; }
        }
        """;

    [Fact]
    public void FragmentParamBesideBracketedChildren_OrdersSlotsBySourcePosition()
    {
        // Not a divergence from the method surface that is merely tolerated. The indexer returns View, so
        // nothing can be chained after the brackets and children are always written last — source order is
        // the only order this surface can produce. AGENTS.md's rule, that sequence numbers represent source
        // syntax positions, is what makes it the correct one. The method form writes children first and so
        // emits ChildContent first; each spelling is faithful to its own source.
        var result = CompilationTestHost.RunGenerator(
            ("Host.cs", """
                using BlazorCodeFirst;
                using static BlazorCodeFirst.Html;

                namespace T;

                public partial class Host : ComposeComponentBase
                {
                    protected override View Body =>
                        Component<Panel>().Param(c => c.Footer, Div["f"])[Div["x"]];
                }
                """),
            ("Panel.cs", PanelSource));

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString();

        Assert.Contains("__builder.AddComponentParameter(1, \"Footer\"", generated);
        Assert.Contains("__builder.AddComponentParameter(4, \"ChildContent\"", generated);
        CompilationTestHost.AssertOutputCompiles(result);
    }
}

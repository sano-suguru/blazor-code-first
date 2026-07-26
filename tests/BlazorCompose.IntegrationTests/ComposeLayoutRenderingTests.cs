using System.Collections.Generic;
using BlazorCompose.IntegrationTests.Components;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace BlazorCompose.IntegrationTests;

// The whole layout design rests on Blazor delivering the routed page to a Compose layout under a
// parameter named exactly "Body" (LayoutComponentBase.BodyPropertyName, which LayoutView passes by
// name). Unit tests cannot verify that: ComponentBase.SetParametersAsync calls StateHasChanged(),
// which throws without an attached RenderHandle, so these tests drive the real RouteView/LayoutView
// through bUnit instead.
public sealed class ComposeLayoutRenderingTests : BunitContext
{
    [Fact]
    public void Layout_WrapsRoutedPage_ThroughRealLayoutView()
    {
        // RouteView resolves the layout from LayoutProbePage's own [Layout(typeof(ProbeLayout))]
        // attribute (Microsoft.AspNetCore.Components.RouteView does this via reflection), then wraps it
        // in a real LayoutView that passes the page as Body. If SetParametersAsync did not deliver Body
        // by name — the entire premise of ComposeLayoutBase — .page-content would never appear inside
        // .shell .page.
        var routeData = new RouteData(typeof(LayoutProbePage), new Dictionary<string, object?>());

        var cut = Render<RouteView>(parameters => parameters.Add(p => p.RouteData, routeData));

        Assert.Equal("page content", cut.Find(".shell .page .page-content").TextContent);
    }

    [Fact]
    public void Layout_WithCascadingParameter_ReceivesBothBodyAndCascadedValue()
    {
        // Regression test for the design inversion trigger: an earlier approach considered forwarding
        // Body through a hand-built ParameterView (e.g. ParameterView.FromDictionary), which drops
        // cascading-value delivery. The accepted design instead lets Blazor bind Body as an ordinary
        // inherited [Parameter], so a real CascadingValue, an explicit [Parameter], and Body must all
        // reach ProbeLayout in the same render.
        RenderFragment body = builder =>
        {
            builder.OpenComponent<LayoutProbePage>(0);
            builder.CloseComponent();
        };

        var cut = Render<ProbeLayout>(parameters => parameters
            .AddCascadingValue("dark")
            .Add(p => p.Label, "Docs")
            .Add(p => p.Body, body));

        Assert.Equal("Docs", cut.Find(".label").TextContent);
        Assert.Equal("dark", cut.Find(".theme").TextContent);
        Assert.Equal("page content", cut.Find(".page-content").TextContent);
    }

    [Fact]
    public void Layout_WithNullBody_RendersChromeWithoutThrowing()
    {
        // No Body is supplied at all (LayoutComponentBase.Body defaults to null). The generated
        // RenderView calls AddContent(seq, RenderFragment?) with a null fragment, which Blazor's
        // RenderTreeBuilder emits as zero frames — so <main> must render present but empty, and nothing
        // should throw.
        var cut = Render<ProbeLayout>(parameters => parameters.Add(p => p.Label, "Empty"));

        cut.Find(".shell");
        Assert.Equal("Empty", cut.Find(".label").TextContent);
        Assert.Equal("", cut.Find("main").TextContent);
        Assert.Empty(cut.Find("main").Children);
    }

    [Fact]
    public void FragmentContent_TogglingBetweenNullAndNonNull_PreservesSiblingComponentState()
    {
        // Sequence-stability check: toggling ChildContentHostComponent's ChildContent between a
        // fragment and null must not disturb its sibling StatefulRowComponent's component instance.
        // A diff bug that treats the null<->non-null transition as removing/re-adding surrounding
        // frames would reset the sibling's internal counter to 0; this test would then fail.
        var cut = Render<ToggleableChildContentComponent>();

        Assert.Equal("kid", cut.Find(".card").TextContent);

        // Establish sibling state before any toggle.
        cut.FindAll("button")[0].Click();
        cut.FindAll("button")[0].Click();
        Assert.Equal("sibling:2", cut.Find("span").TextContent);

        // Toggle ChildContent to null: .card must render empty, sibling state must be untouched.
        cut.FindAll("button")[1].Click();
        Assert.Equal("", cut.Find(".card").TextContent);
        Assert.Equal("sibling:2", cut.Find("span").TextContent);

        // Toggle back to non-null: sibling state must still be preserved.
        cut.FindAll("button")[1].Click();
        Assert.Equal("kid", cut.Find(".card").TextContent);
        Assert.Equal("sibling:2", cut.Find("span").TextContent);
    }

    [Fact]
    public void ComposeComponentWithChildContent_RendersChildren()
    {
        var cut = Render<ChildContentHostComponent>(parameters => parameters.AddChildContent("<em>kid</em>"));

        Assert.Equal("kid", cut.Find(".card em").TextContent);
    }
}

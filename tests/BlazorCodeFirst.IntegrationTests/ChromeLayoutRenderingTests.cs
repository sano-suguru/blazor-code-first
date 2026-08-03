using System.Collections.Generic;
using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace BlazorCodeFirst.IntegrationTests;

// The whole layout design rests on Blazor delivering the routed page to a BlazorCodeFirst layout under a
// parameter named exactly "Body" (LayoutComponentBase.BodyPropertyName, which LayoutView passes by
// name). Unit tests cannot verify that: ComponentBase.SetParametersAsync calls StateHasChanged(),
// which throws without an attached RenderHandle, so these tests drive the real RouteView/LayoutView
// through bUnit instead.
public sealed class ChromeLayoutRenderingTests : BunitContext
{
    [Fact]
    public void Layout_WrapsRoutedPage_ThroughRealLayoutView()
    {
        // RouteView resolves the layout from LayoutProbePage's own [Layout(typeof(ProbeLayout))]
        // attribute (Microsoft.AspNetCore.Components.RouteView does this via reflection), then wraps it
        // in a real LayoutView that passes the page as Body. If SetParametersAsync did not deliver Body
        // by name, the entire premise of ChromeLayoutBase, .page-content would never appear inside
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
        // RenderTreeBuilder emits as zero frames, so <main> must render present but empty, and nothing
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
        // Sequence-stability check exercised inside ONE BlazorCodeFirst-generated Body: a conditional/nullable
        // RenderFragment position (`_show ? Slot : null`) sits next to a stateful sibling component in
        // the same render tree, so both get their sequence numbers from the same static allocation pass
        // (see ToggleableFragmentShellComponent). A bug that let the fragment's null<->non-null
        // transition shift the sibling's allocated sequence number would make Blazor treat the sibling
        // as a new component instance on toggle, resetting its internal counter to 0; this test would
        // then fail. Two independently-diffed component instances would not exercise this: Blazor
        // already isolates component boundaries from each other regardless of what BlazorCodeFirst does,
        // so only a shared render tree can prove the generator's own allocation is stable.
        var cut = Render<ToggleableFragmentShellComponent>();

        Assert.Contains("kid", cut.Markup);

        // Establish sibling state before any toggle.
        cut.FindAll("button")[0].Click();
        cut.FindAll("button")[0].Click();
        Assert.Equal("sibling:2", cut.Find("span").TextContent);

        // Toggle the fragment to null: "kid" must disappear, sibling state must be untouched.
        cut.FindAll("button")[1].Click();
        Assert.DoesNotContain("kid", cut.Markup);
        Assert.Equal("sibling:2", cut.Find("span").TextContent);

        // Toggle back to non-null: "kid" returns, sibling state still preserved.
        cut.FindAll("button")[1].Click();
        Assert.Contains("kid", cut.Markup);
        Assert.Equal("sibling:2", cut.Find("span").TextContent);
    }

    [Fact]
    public void BodyComponentWithChildContent_RendersChildren()
    {
        var cut = Render<ChildContentHostComponent>(parameters => parameters.AddChildContent("<em>kid</em>"));

        Assert.Equal("kid", cut.Find(".card em").TextContent);
    }

    [Fact]
    public void DerivedLayoutOverridingChrome_RendersItsOwnChromeNotTheBase()
    {
        // Both levels are partial, so both get their own generated RenderView and the derived Chrome
        // wins, the correct outcome, pinned here because the inheritance shape is what makes BCF1001
        // load-bearing (spec F16): RenderView is emitted as an `override`, so a derived layout that
        // did NOT get its own generated RenderView, because it was not partial, would inherit the
        // base one and render the BASE chrome while silently ignoring its own Chrome, with no CS0534
        // to warn anyone, since RenderView is implemented in the base. That failure cannot be written
        // as a test: BCF1001 stops it at compile time. The generator must therefore keep reporting
        // BCF1001 for a derived class that declares the override.
        var routeData = new RouteData(typeof(DerivedShellProbePage), new Dictionary<string, object?>());

        var cut = Render<RouteView>(parameters => parameters.Add(p => p.RouteData, routeData));

        Assert.Equal("derived", cut.Find(".derived-chrome").TextContent);
        Assert.Empty(cut.FindAll(".base-chrome"));
    }

    [Fact]
    public void NestedChromeLayouts_WrapThePageInBothLevels()
    {
        var routeData = new RouteData(typeof(NestedLayoutProbePage), new Dictionary<string, object?>());

        var cut = Render<RouteView>(parameters => parameters.Add(p => p.RouteData, routeData));

        // Outer chrome contains the inner chrome, which contains the page.
        Assert.Equal(
            "nested page content",
            cut.Find(".outer .outer-slot .inner .inner-slot .nested-page-content").TextContent);
    }
}

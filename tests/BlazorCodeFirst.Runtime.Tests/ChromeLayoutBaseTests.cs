using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class ChromeLayoutBaseTests
{
    // RenderView is hand-written here: the generator does not recognize ChromeLayoutBase until the
    // compiler task lands, and this test is about the runtime contract, not code generation.
    private sealed class ProbeLayout : ChromeLayoutBase
    {
        public int RenderViewCallCount { get; private set; }

        protected override View Chrome => default;

        protected override void RenderView(RenderTreeBuilder builder)
        {
            RenderViewCallCount++;
            builder.AddContent(0, Body);
        }

        public void Render(RenderTreeBuilder builder) => BuildRenderTree(builder);
    }

    [Fact]
    public void ChromeLayoutBase_DerivesFromLayoutComponentBase()
    {
        // Inheriting LayoutComponentBase gives us Blazor's Body parameter under the exact name
        // LayoutView passes (LayoutComponentBase.BodyPropertyName) plus its [DynamicDependency]
        // trimmer hint, with no parameter plumbing of our own.
        Assert.True(typeof(LayoutComponentBase).IsAssignableFrom(typeof(ChromeLayoutBase)));
    }

    [Fact]
    public void BodyParameter_IsInheritedFromLayoutComponentBase()
    {
        // The Body parameter is inherited from LayoutComponentBase and is therefore visible with the
        // correct name, type, and attribute. The end-to-end by-name binding contract (Blazor's LayoutView
        // passes Body by the literal string "Body" and SetParametersAsync delivers it) is verified in the
        // integration tests, which drive the real RouteView and LayoutView with a routed page; SetParametersAsync
        // requires an attached RenderHandle that a unit-test instance does not have.
        var property = typeof(ProbeLayout).GetProperty("Body");

        Assert.NotNull(property);
        Assert.Equal(typeof(RenderFragment), property!.PropertyType);
        Assert.NotNull(property.GetCustomAttributes(typeof(ParameterAttribute), inherit: true).FirstOrDefault());
    }

    [Fact]
    public void BuildRenderTree_WhenRendered_DelegatesToRenderView()
    {
        var layout = new ProbeLayout();
        var builder = new RenderTreeBuilder();

        layout.Render(builder);

        Assert.Equal(1, layout.RenderViewCallCount);
    }
}

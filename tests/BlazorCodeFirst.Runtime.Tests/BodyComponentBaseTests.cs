using BlazorCodeFirst;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.Runtime.Tests;

public sealed class BodyComponentBaseTests
{
    [Fact]
    public void BuildRenderTree_WhenRendered_DelegatesToGeneratedRenderView()
    {
        var component = new TestComponent();
        var builder = new RenderTreeBuilder();

        component.Render(builder);

        Assert.Equal(1, component.RenderViewCalls);
    }

    [Fact]
    public void BuildRenderTree_WhenRendered_DoesNotEvaluateBody()
    {
        var component = new BodyThrowsComponent();
        var builder = new RenderTreeBuilder();

        component.Render(builder);

        Assert.Equal(1, component.RenderViewCalls);
    }

    [Fact]
    public void HtmlSurface_WhenInvoked_RemainsInertAtRuntime()
    {
        var onClickCalled = false;

        _ = Html.Span["Hello"];
        _ = Html.Button.OnClick(() => onClickCalled = true)["Increment"];
        _ = Html.Div[
            Html.Span["Child"],
            Html.If(
                condition: true,
                then: static () => throw new InvalidOperationException("Then branch should not run."),
                otherwise: static () => throw new InvalidOperationException("Else branch should not run."))];

        Assert.False(onClickCalled);
    }

    private sealed class TestComponent : BodyComponentBase
    {
        public int RenderViewCalls { get; private set; }

        protected override View Body => default;

        protected override void RenderView(RenderTreeBuilder builder) => RenderViewCalls++;

        public void Render(RenderTreeBuilder builder) => BuildRenderTree(builder);
    }

    private sealed class BodyThrowsComponent : BodyComponentBase
    {
        public int RenderViewCalls { get; private set; }

        protected override View Body => throw new InvalidOperationException("Body should remain inert at runtime.");

        protected override void RenderView(RenderTreeBuilder builder) => RenderViewCalls++;

        public void Render(RenderTreeBuilder builder) => BuildRenderTree(builder);
    }
}

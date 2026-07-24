using BlazorCompose;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCompose.Runtime.Tests;

public sealed class ComposeComponentBaseTests
{
    [Fact]
    public void BuildRenderTree_WhenRendered_DelegatesToGeneratedRenderBody()
    {
        var component = new TestComponent();
        var builder = new RenderTreeBuilder();

        component.Render(builder);

        Assert.Equal(1, component.RenderBodyCalls);
    }

    [Fact]
    public void BuildRenderTree_WhenRendered_DoesNotEvaluateBody()
    {
        var component = new BodyThrowsComponent();
        var builder = new RenderTreeBuilder();

        component.Render(builder);

        Assert.Equal(1, component.RenderBodyCalls);
    }

    [Fact]
    public void HtmlFactories_WhenInvoked_RemainInertAtRuntime()
    {
        var onClickCalled = false;

        _ = Html.Span("Hello");
        _ = Html.Button("Increment").OnClick(() => onClickCalled = true);
        _ = Html.Div(
            Html.Span("Child"),
            Html.If(
                condition: true,
                then: static () => throw new InvalidOperationException("Then branch should not run."),
                otherwise: static () => throw new InvalidOperationException("Else branch should not run.")));

        Assert.False(onClickCalled);
    }

    private sealed class TestComponent : ComposeComponentBase
    {
        public int RenderBodyCalls { get; private set; }

        protected override View Body => default;

        protected override void RenderBody(RenderTreeBuilder builder) => RenderBodyCalls++;

        public void Render(RenderTreeBuilder builder) => BuildRenderTree(builder);
    }

    private sealed class BodyThrowsComponent : ComposeComponentBase
    {
        public int RenderBodyCalls { get; private set; }

        protected override View Body => throw new InvalidOperationException("Body should remain inert at runtime.");

        protected override void RenderBody(RenderTreeBuilder builder) => RenderBodyCalls++;

        public void Render(RenderTreeBuilder builder) => BuildRenderTree(builder);
    }
}

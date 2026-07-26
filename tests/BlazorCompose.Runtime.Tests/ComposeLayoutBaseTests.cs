using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCompose.Runtime.Tests;

public sealed class ComposeLayoutBaseTests
{
    // RenderView is hand-written here: the generator does not recognize ComposeLayoutBase until the
    // compiler task lands, and this test is about the runtime contract, not code generation.
    private sealed class ProbeLayout : ComposeLayoutBase
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
    public void ComposeLayoutBase_DerivesFromLayoutComponentBase()
    {
        // Inheriting LayoutComponentBase gives us Blazor's Body parameter under the exact name
        // LayoutView passes (LayoutComponentBase.BodyPropertyName) plus its [DynamicDependency]
        // trimmer hint, with no parameter plumbing of our own.
        Assert.True(typeof(LayoutComponentBase).IsAssignableFrom(typeof(ComposeLayoutBase)));
    }

    [Fact]
    public void BodyParameter_IsInheritedAndSettableByName()
    {
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

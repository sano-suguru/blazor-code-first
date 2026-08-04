using BlazorCodeFirst.IntegrationTests.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.IntegrationTests.DiffCost;

public sealed class EditCountingRendererTests
{
    [Fact]
    public async Task Renderer_AfterFirstRender_RecordsOneBatch()
    {
        using var renderer = new EditCountingRenderer();
        var component = new ToggleProbe();

        int id = renderer.Attach(component);
        await renderer.InvokeAsync(() => renderer.RenderAsync(id));

        Assert.Single(renderer.Batches);
    }

    [Fact]
    public async Task Renderer_WhenStateChanges_RecordsASecondBatchWithTheDiffEdits()
    {
        using var renderer = new EditCountingRenderer();
        var component = new ToggleProbe();

        int id = renderer.Attach(component);
        await renderer.InvokeAsync(() => renderer.RenderAsync(id));
        await renderer.InvokeAsync(component.Toggle);

        Assert.Equal(2, renderer.Batches.Count);

        // Toggling adds one <p> inside the div: prepend the frame, and step in and out of the div.
        var edits = renderer.Batches[1];
        Assert.Equal(1, edits[RenderTreeEditType.PrependFrame]);
        Assert.Equal(3, EditCountingRenderer.TotalEdits(edits));
    }

    [Fact]
    public async Task CountingRow_WhenRenderedAndNotTornDown_InitializesOnceAndIsNotDisposed()
    {
        using var renderer = new EditCountingRenderer();
        var log = new RowLifecycleLog();
        var component = new SingleRowProbe { Lifecycle = log };

        int id = renderer.Attach(component);
        await renderer.InvokeAsync(() => renderer.RenderAsync(id));

        Assert.Equal(1, log.Initialized);
        Assert.Equal(0, log.Disposed);
    }

    private sealed class ToggleProbe : ComponentBase
    {
        private bool _show;

        public void Toggle()
        {
            _show = !_show;
            StateHasChanged();
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.OpenRegion(1);
            if (_show)
            {
                builder.OpenElement(2, "p");
                builder.CloseElement();
            }

            builder.CloseRegion();
            builder.CloseElement();
        }
    }

    private sealed class SingleRowProbe : ComponentBase
    {
        public RowLifecycleLog Lifecycle { get; set; } = new();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CountingRowComponent>(0);
            builder.AddComponentParameter(1, nameof(CountingRowComponent.Label), "r0");
            builder.AddComponentParameter(2, nameof(CountingRowComponent.Lifecycle), Lifecycle);
            builder.CloseComponent();
        }
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorCodeFirst.Benchmarks;

/// <summary>
/// A <see cref="Renderer"/> that drives render batches to completion and discards them.
/// </summary>
/// <remarks>
/// This is the second of §7.1's two boundaries. The first measures the generated
/// <c>BuildRenderTree</c> alone, isolating what the compilation strategy allocates; this one measures
/// a whole render cycle including the diff, which is the figure an application actually pays. §7.1
/// makes two separate claims — that the SSC path matches an equivalent Razor component, and that the
/// design adds no GC load of its own — and neither is supported by the other boundary alone.
/// </remarks>
internal sealed class BatchRenderer : Renderer
{
    public BatchRenderer()
        : base(EmptyServiceProvider.Instance, NullLoggerFactory.Instance)
    {
    }

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    public int Attach(IComponent component) => AssignRootComponentId(component);

    public Task RenderAsync(int componentId) => RenderRootComponentAsync(componentId);

    /// <summary>Attaches a root component and performs its one initial render, synchronously.</summary>
    /// <remarks>
    /// For <c>[GlobalSetup]</c> use: a root component id may be rendered through
    /// <see cref="RenderAsync"/> only once, so the cycle benchmarks drive re-renders through
    /// <c>Invalidate</c> instead.
    /// </remarks>
    public void RenderOnce(IComponent component)
    {
        int id = Attach(component);
        Dispatcher.InvokeAsync(() => RenderAsync(id)).GetAwaiter().GetResult();
    }

    protected override void HandleException(Exception exception) =>
        throw new InvalidOperationException("A component threw while rendering.", exception);

    // Returning a completed task keeps the measurement on the render path: nothing here allocates,
    // so the reported bytes are the renderer's and the component's, not this harness's.
    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}

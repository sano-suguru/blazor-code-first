using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlazorCodeFirst.IntegrationTests.DiffCost;

/// <summary>
/// A <see cref="Renderer"/> that records, per render batch, how many edits of each
/// <see cref="RenderTreeEditType"/> Blazor's diff produced.
/// </summary>
/// <remarks>
/// This is the instrument for DESIGN.md §7.2. That section claims a complexity class,
/// O(|r_t| + |r_{t+1}|), and component state loss — neither of which is a duration. A wall-clock
/// figure at one input size cannot demonstrate a complexity class and would carry machine noise into
/// a number published in DESIGN.md, whereas the edit count is deterministic on any machine and shows
/// subtree teardown directly in the per-type breakdown.
/// <para>
/// bUnit is not used here: it has its own renderer and surfaces markup, not the batch. The claim is
/// about the batch.
/// </para>
/// </remarks>
public sealed class EditCountingRenderer : Renderer
{
    private readonly List<IReadOnlyDictionary<RenderTreeEditType, int>> _batches = [];

    public EditCountingRenderer()
        : base(EmptyServiceProvider.Instance, NullLoggerFactory.Instance)
    {
    }

    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    /// <summary>Edit tallies in batch order. The first entry is the initial render.</summary>
    public IReadOnlyList<IReadOnlyDictionary<RenderTreeEditType, int>> Batches => _batches;

    /// <summary>Registers a component instance and returns its component id.</summary>
    public int Attach(IComponent component) => AssignRootComponentId(component);

    /// <summary>Renders the component registered under <paramref name="componentId"/>.</summary>
    public Task RenderAsync(int componentId) => RenderRootComponentAsync(componentId);

    /// <summary>Runs <paramref name="action"/> on the renderer's dispatcher.</summary>
    public Task InvokeAsync(Action action) => Dispatcher.InvokeAsync(action);

    /// <summary>Runs <paramref name="action"/> on the renderer's dispatcher.</summary>
    public Task InvokeAsync(Func<Task> action) => Dispatcher.InvokeAsync(action);

    /// <summary>Sums a batch's per-type tallies.</summary>
    public static int TotalEdits(IReadOnlyDictionary<RenderTreeEditType, int> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        int total = 0;
        foreach (int count in batch.Values)
        {
            total += count;
        }

        return total;
    }

    protected override void HandleException(Exception exception) =>
        throw new InvalidOperationException("A component threw while rendering.", exception);

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        var tally = new Dictionary<RenderTreeEditType, int>();

        for (int i = 0; i < renderBatch.UpdatedComponents.Count; i++)
        {
            ref var diff = ref renderBatch.UpdatedComponents.Array[i];
            for (int e = 0; e < diff.Edits.Count; e++)
            {
                var type = diff.Edits.Array[diff.Edits.Offset + e].Type;
                tally[type] = tally.GetValueOrDefault(type) + 1;
            }
        }

        _batches.Add(tally);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Supplies no services at all. <see cref="Renderer"/> resolves optional services such as
    /// <c>IComponentActivator</c> from the provider and falls back to its defaults on null, and the
    /// components in this harness are constructed directly by the tests.
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}

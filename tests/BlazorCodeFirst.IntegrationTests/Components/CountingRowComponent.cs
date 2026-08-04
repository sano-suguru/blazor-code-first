using BlazorCodeFirst.IntegrationTests.DiffCost;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// A child row that reports its own construction and teardown, and renders a distinguishing label.
/// </summary>
/// <remarks>
/// Hand-written rather than built from a BlazorCodeFirst <c>Body</c>, so the fixture for the §7.2
/// measurement does not depend on the mechanism under measurement — the same reason
/// <see cref="StatefulRowComponent"/> is hand-written.
/// <para>
/// The label matters to the measurement. With rows that render identical content the diff has nothing
/// to update, and the unkeyed runtime-increment variant collapses to a constant edit count regardless
/// of list length, understating its cost. Real rows differ; these do too.
/// </para>
/// </remarks>
public sealed class CountingRowComponent : ComponentBase, IDisposable
{
    [Parameter] public string Label { get; set; } = "";

    [Parameter] public RowLifecycleLog Lifecycle { get; set; } = new();

    protected override void OnInitialized() => Lifecycle.Initialized++;

    public void Dispose() => Lifecycle.Disposed++;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "span");
        builder.AddContent(1, Label);
        builder.CloseElement();
    }
}

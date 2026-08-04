using BlazorCodeFirst.IntegrationTests.DiffCost;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// Variant B of the §7.2 comparison: variant A's frames exactly, with every sequence number taken
/// from a runtime counter instead of a static literal.
/// </summary>
/// <remarks>
/// This variant exists to make the measurement attributable. A comparison against the region-free
/// naive method (variant C) cannot separate two mechanisms — static sequence assignment and the
/// region isolation DESIGN.md §5.3 describes — because C differs from A in both. B differs in exactly
/// one, so <c>VariantEquivalenceTests</c> can hold it to a frame-for-frame match against A.
/// <para>
/// No real library emits regions with runtime-incremented region sequences, so B is a control rather
/// than the comparison DESIGN.md §9 asks for. What it isolates is that <c>OpenRegion(seq++)</c> shifts
/// the region frame's own sequence, so the region holding the rows is torn down and keys cannot help:
/// keyed matching happens within a sibling group, and here the group itself moved.
/// </para>
/// </remarks>
public sealed class RegionedIncrementBannerListComponent : ComponentBase
{
    public int RowCount { get; set; } = 10;

    public RowLifecycleLog Lifecycle { get; set; } = new();

    /// <summary>When false, rows carry no key, so keyed matching cannot rescue them.</summary>
    public bool Keyed { get; set; } = true;

    /// <summary>Mirrors <see cref="BannerListComponent.ShowBanner"/>; see the remarks there.</summary>
    public bool ShowBanner { get; set; }

    public void ToggleBanner()
    {
        ShowBanner = !ShowBanner;
        StateHasChanged();
    }

    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        int sequence = 0;

        builder.OpenElement(sequence++, "div");

        builder.OpenRegion(sequence++);
        if (ShowBanner)
        {
            builder.OpenElement(sequence++, "p");
            builder.AddContent(sequence++, "banner");
            builder.CloseElement();
        }

        builder.CloseRegion();

        builder.OpenRegion(sequence++);
        foreach (int i in Enumerable.Range(0, RowCount))
        {
            builder.OpenComponent<CountingRowComponent>(sequence++);
            if (Keyed)
            {
                builder.SetKey(i);
            }

            builder.AddComponentParameter(sequence++, nameof(CountingRowComponent.Label), $"r{i}");
            builder.AddComponentParameter(sequence++, nameof(CountingRowComponent.Lifecycle), Lifecycle);
            builder.CloseComponent();
        }

        builder.CloseRegion();
        builder.CloseElement();
    }
}

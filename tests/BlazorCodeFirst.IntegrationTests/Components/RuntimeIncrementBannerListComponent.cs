using BlazorCodeFirst.IntegrationTests.DiffCost;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// Variant C of the §7.2 comparison: the 動的インクリメント方式 that section names. Sequence numbers
/// come from a runtime counter and no regions are emitted, which is how hand-written
/// <c>RenderTreeBuilder</c> code and runtime-tree libraries actually work.
/// </summary>
/// <remarks>
/// <see cref="BannerFrameWidth"/> is settable because the measured outcome depends on a structural
/// coincidence. The banner shifts every following sequence number by its frame width; when that shift
/// is a multiple of the row width, row <c>i</c> lines up with row <c>i+1</c> and the diff updates text
/// instead of tearing components down. A row here is three frames wide (component, label parameter,
/// lifecycle parameter), so a two-frame banner does not align and a three-frame banner does. Measuring
/// only one of them would publish a figure that silently depends on the arithmetic.
/// </remarks>
public sealed class RuntimeIncrementBannerListComponent : ComponentBase
{
    public int RowCount { get; set; } = 10;

    public RowLifecycleLog Lifecycle { get; set; } = new();

    /// <summary>When false, rows carry no key, so keyed matching cannot rescue them.</summary>
    public bool Keyed { get; set; } = true;

    /// <summary>Mirrors <see cref="BannerListComponent.ShowBanner"/>; see the remarks there.</summary>
    public bool ShowBanner { get; set; }

    /// <summary>
    /// Frames the banner occupies when shown: 2 for <c>p</c> plus text, 3 to add a class attribute.
    /// Variant A's banner is 2, so the equivalence gate against A only applies at 2.
    /// </summary>
    public int BannerFrameWidth { get; set; } = 2;

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

        if (ShowBanner)
        {
            builder.OpenElement(sequence++, "p");
            if (BannerFrameWidth >= 3)
            {
                builder.AddAttribute(sequence++, "class", "banner");
            }

            builder.AddContent(sequence++, "banner");
            builder.CloseElement();
        }

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

        builder.CloseElement();
    }
}

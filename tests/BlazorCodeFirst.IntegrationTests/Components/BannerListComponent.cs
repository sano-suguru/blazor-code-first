using BlazorCodeFirst.IntegrationTests.DiffCost;
using Microsoft.AspNetCore.Components.Rendering;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// Variant A of the §7.2 comparison: a conditional banner ahead of a keyed row list, compiled by the
/// source generator so its sequence numbers are static literals.
/// </summary>
/// <remarks>
/// The banner sits ahead of the rows deliberately. ARCHITECTURE.md §1.2 names the condition under
/// which runtime increment breaks — "条件付きレンダリングや要素挿入により π と生成順序の対応が崩れた
/// 時点" — and a keyed-list reorder does not meet it, because generation order stays 0..n whatever the
/// item order. Inserting an element ahead of existing siblings does.
/// <para>
/// The banner is two frames wide (<c>p</c> plus its text). Variant C's banner width is settable
/// because whether the shift is a multiple of the row width changes what the diff does; see
/// <see cref="RuntimeIncrementBannerListComponent"/>.
/// </para>
/// </remarks>
public partial class BannerListComponent : BodyComponentBase
{
    /// <summary>Number of rows, the N of the complexity sweep.</summary>
    public int RowCount { get; set; } = 10;

    /// <summary>Shared teardown counter handed to every row.</summary>
    public RowLifecycleLog Lifecycle { get; set; } = new();

    /// <summary>
    /// Whether the banner is rendered ahead of the rows.
    /// </summary>
    /// <remarks>
    /// Settable independently of <see cref="ToggleBanner"/> because the frame-equivalence gate reads
    /// frames from a component that was never attached to a renderer, and
    /// <c>ComponentBase.StateHasChanged</c> throws without a render handle.
    /// </remarks>
    public bool ShowBanner { get; set; }

    private IEnumerable<int> Rows => Enumerable.Range(0, RowCount);

    protected override View Body =>
        Div[
            If(ShowBanner, () => P["banner"]),
            ForEach(Rows,
                key: i => i,
                content: i => Component<CountingRowComponent>()
                    .Param(r => r.Label, $"r{i}")
                    .Param(r => r.Lifecycle, Lifecycle))];

    /// <summary>
    /// Inserts or removes the banner ahead of the rows and requests a re-render. Requires the
    /// component to be attached to a renderer.
    /// </summary>
    public void ToggleBanner()
    {
        ShowBanner = !ShowBanner;
        StateHasChanged();
    }

    /// <summary>Exposes the generated render path so the frame-equivalence gate can read its frames.</summary>
    public void Build(RenderTreeBuilder builder) => BuildRenderTree(builder);
}

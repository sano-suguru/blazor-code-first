using BlazorCodeFirst.IntegrationTests.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Xunit.Abstractions;

namespace BlazorCodeFirst.IntegrationTests.DiffCost;

/// <summary>
/// The measurement behind DESIGN.md §7.2. Each test asserts a value and prints it, so the figures
/// published in DESIGN.md can be reproduced by running this class and reading the output.
/// </summary>
public sealed class DiffCostTests(ITestOutputHelper output)
{
    private static readonly int[] RowCounts = [10, 100, 1000];

    [Fact]
    public async Task GeneratedComponent_WhenBannerInserted_CostsThreeEditsAtEveryListLength()
    {
        // §7.2's complexity claim: the diff stays at the theoretical minimum, independent of N.
        foreach (int rowCount in RowCounts)
        {
            var log = new RowLifecycleLog();
            var component = new BannerListComponent { RowCount = rowCount, Lifecycle = log };

            var measured = await MeasureToggleAsync(component, component.ToggleBanner, log);

            output.WriteLine($"A generated      N={rowCount,4} edits={measured.TotalEdits,5} {Format(measured.Edits)}");
            Assert.Equal(3, measured.TotalEdits);
        }
    }

    [Fact]
    public async Task GeneratedComponent_WhenBannerInserted_DestroysNoRows()
    {
        // §7.2's state claim, measured directly: a row that is never disposed kept its state.
        foreach (int rowCount in RowCounts)
        {
            var log = new RowLifecycleLog();
            var component = new BannerListComponent { RowCount = rowCount, Lifecycle = log };

            var measured = await MeasureToggleAsync(component, component.ToggleBanner, log);

            output.WriteLine(
                $"A generated      N={rowCount,4} initialized={measured.Initialized,5} disposed={measured.Disposed,5}");
            Assert.Equal(0, measured.Disposed);
            Assert.Equal(0, measured.Initialized);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NaiveMethod_WhenUnkeyed_DegradesToLinearCost(bool wideBanner)
    {
        // The comparison §9 asks for, and the reason both banner widths are measured. Edit cost is
        // linear in N either way, which is what carries §7.2's complexity claim. How much state is
        // lost is not: a three-frame banner shifts by exactly the row width, so row i lands on row
        // i+1 and the diff rewrites text instead of tearing components down, leaving one teardown at
        // the tail. A two-frame banner does not divide the row width and every row is destroyed.
        int bannerFrameWidth = wideBanner ? 3 : 2;

        foreach (int rowCount in RowCounts)
        {
            var log = new RowLifecycleLog();
            var component = new RuntimeIncrementBannerListComponent
            {
                RowCount = rowCount,
                Lifecycle = log,
                Keyed = false,
                BannerFrameWidth = bannerFrameWidth,
            };

            var measured = await MeasureToggleAsync(component, component.ToggleBanner, log);

            output.WriteLine(
                $"C naive unkeyed  banner={bannerFrameWidth} N={rowCount,4} " +
                $"edits={measured.TotalEdits,5} initialized={measured.Initialized,5} " +
                $"disposed={measured.Disposed,5} {Format(measured.Edits)}");

            Assert.Equal((3 * rowCount) + 3, measured.TotalEdits);
            Assert.Equal(wideBanner ? 1 : rowCount, measured.Disposed);
            Assert.Equal(measured.Disposed, measured.Initialized);
        }
    }

    [Fact]
    public async Task NaiveMethod_WhenKeyed_MatchesTheGeneratedComponent()
    {
        // The result that contradicts §7.2's stated reason. Keys alone hold the cost at O(1) and keep
        // every row alive, with no static sequence assignment involved. Html.ForEach makes a key
        // mandatory, so this is the only regime a BlazorCodeFirst author can be in.
        foreach (int rowCount in RowCounts)
        {
            var log = new RowLifecycleLog();
            var component = new RuntimeIncrementBannerListComponent
            {
                RowCount = rowCount,
                Lifecycle = log,
                Keyed = true,
            };

            var measured = await MeasureToggleAsync(component, component.ToggleBanner, log);

            output.WriteLine(
                $"C naive keyed    N={rowCount,4} edits={measured.TotalEdits,5} " +
                $"initialized={measured.Initialized,5} disposed={measured.Disposed,5} {Format(measured.Edits)}");
            Assert.Equal(3, measured.TotalEdits);
            Assert.Equal(0, measured.Disposed);
            Assert.Equal(0, measured.Initialized);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RuntimeIncrementWithRegions_RegardlessOfKeying_DestroysEveryRow(bool keyed)
    {
        // Variant B, the control. The region holding the rows takes its sequence from the counter, so
        // inserting the banner moves the region itself and keyed matching inside it cannot help.
        foreach (int rowCount in RowCounts)
        {
            var log = new RowLifecycleLog();
            var component = new RegionedIncrementBannerListComponent
            {
                RowCount = rowCount,
                Lifecycle = log,
                Keyed = keyed,
            };

            var measured = await MeasureToggleAsync(component, component.ToggleBanner, log);

            output.WriteLine(
                $"B regioned {(keyed ? "keyed  " : "unkeyed")} N={rowCount,4} " +
                $"edits={measured.TotalEdits,5} initialized={measured.Initialized,5} " +
                $"disposed={measured.Disposed,5} {Format(measured.Edits)}");
            Assert.Equal((3 * rowCount) + 3, measured.TotalEdits);
            Assert.Equal(rowCount, measured.Disposed);
            Assert.Equal(rowCount, measured.Initialized);
        }
    }

    /// <summary>What one banner toggle cost: the batch's edits, and the row lifecycle it caused.</summary>
    private sealed record ToggleMeasurement(
        IReadOnlyDictionary<RenderTreeEditType, int> Edits,
        int Initialized,
        int Disposed)
    {
        public int TotalEdits => EditCountingRenderer.TotalEdits(Edits);
    }

    /// <summary>
    /// Renders <paramref name="component"/>, zeroes the lifecycle counts so the initial render is
    /// excluded, then applies <paramref name="mutate"/> and reports what the resulting batch cost.
    /// </summary>
    /// <remarks>
    /// The lifecycle counts are snapshotted into the returned record before this method's
    /// <c>using</c> scope ends. Disposing the renderer disposes every component it still holds, which
    /// calls <c>Dispose</c> on all N rows; reading the counts afterwards would add N to every
    /// disposal figure and make even the generated variant look like it tore its rows down.
    /// </remarks>
    private static async Task<ToggleMeasurement> MeasureToggleAsync(
        IComponent component,
        Action mutate,
        RowLifecycleLog log)
    {
        using var renderer = new EditCountingRenderer();
        int id = renderer.Attach(component);
        await renderer.InvokeAsync(() => renderer.RenderAsync(id));

        log.Reset();
        await renderer.InvokeAsync(mutate);

        Assert.Equal(2, renderer.Batches.Count);
        return new ToggleMeasurement(renderer.Batches[1], log.Initialized, log.Disposed);
    }

    private static string Format(IReadOnlyDictionary<RenderTreeEditType, int> edits) =>
        string.Join(
            " ",
            edits.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
                 .Select(pair => $"{pair.Key}={pair.Value}"));
}

using BlazorCodeFirst.IntegrationTests.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.IntegrationTests.DiffCost;

/// <summary>
/// Gates for the §7.2 comparison. Until these pass, an edit-count difference between the variants
/// could come from a structural mismatch rather than from sequence assignment.
/// </summary>
public sealed class VariantEquivalenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VariantB_AgainstGeneratedVariantA_DiffersOnlyInSequenceNumbers(bool showBanner)
    {
        var log = new RowLifecycleLog();
        var a = new BannerListComponent { RowCount = 3, Lifecycle = log, ShowBanner = showBanner };
        var b = new RegionedIncrementBannerListComponent
        {
            RowCount = 3,
            Lifecycle = log,
            ShowBanner = showBanner,
        };

        var left = new RenderTreeBuilder();
        var right = new RenderTreeBuilder();
        a.Build(left);
        b.Build(right);

        FrameEquivalence.AssertEquivalent(left, right);
    }

    [Fact]
    public void GeneratedVariantA_WhenBannerHidden_KeepsTheRowRegionSequenceItHasWhenShown()
    {
        // The mechanism under test, stated as an assertion rather than left implicit: the region
        // holding the rows must carry the same sequence number in both states. Variant B fails this
        // and that is why it tears the rows down.
        var log = new RowLifecycleLog();
        var hidden = new BannerListComponent { RowCount = 3, Lifecycle = log, ShowBanner = false };
        var shown = new BannerListComponent { RowCount = 3, Lifecycle = log, ShowBanner = true };

        var hiddenBuilder = new RenderTreeBuilder();
        var shownBuilder = new RenderTreeBuilder();
        hidden.Build(hiddenBuilder);
        shown.Build(shownBuilder);

        Assert.Equal(RowRegionSequence(hiddenBuilder), RowRegionSequence(shownBuilder));
    }

    /// <summary>
    /// Returns the sequence number of the last region frame, which is the one wrapping the rows.
    /// </summary>
    private static int RowRegionSequence(RenderTreeBuilder builder)
    {
        var frames = builder.GetFrames();
        int found = -1;
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType == RenderTreeFrameType.Region)
            {
                found = frames.Array[i].Sequence;
            }
        }

        Assert.NotEqual(-1, found);
        return found;
    }
}

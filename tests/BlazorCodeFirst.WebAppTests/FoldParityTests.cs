using BlazorCodeFirst.WebAppTestHost.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.WebAppTests;

/// <summary>
/// Pins the premise that <c>fold-parity.spec.ts</c> relies on for every probe in <c>FoldParityView</c>:
/// the folded container really does collapse to one <c>AddMarkupContent</c> frame, and the unfolded
/// container really does stay a run of element frames. Without this, if a probe's folded spelling ever
/// stopped folding (or its unfolded spelling accidentally started), the browser comparison would still
/// pass while measuring nothing — the same failure mode the #140 benchmark gate and the prerender
/// escaping test both hit earlier in this work. This is the .NET-side half of the browser gate; the
/// browser half is <c>fold-parity.spec.ts</c>, which <c>dotnet test</c> does not run.
/// </summary>
public sealed class FoldParityTests
{
    /// <summary>
    /// Each probe's <see cref="BlazorCodeFirst.WebAppTestHost.Components.TableFragmentProbe.Build"/>-style
    /// method renders exactly two top-level siblings: a fully-static container first, then a
    /// property-driven one. The static container is the sole member of the first fold run, so it must
    /// collapse to exactly one <c>Markup</c> frame at index 0; the property-driven container must remain
    /// an <c>Element</c> frame whose subtree holds exactly <paramref name="expectedUnfoldedFrameCount"/>
    /// frames.
    /// </summary>
    /// <remarks>
    /// The count must be exact, not merely "not 1". A partially-folded unfolded side still opens with an
    /// <c>Element</c> frame at the root — folding only ever removes a run of frames *inside* that root,
    /// it does not fold the root itself away when even one descendant is non-constant — so it still
    /// passes both the <c>FrameType.Element</c> check and a bare <c>NotEqual(1, …)</c> check. That gap is
    /// not hypothetical: <c>VoidTagInRunProbe</c>'s first draft folded its lone, fully-literal
    /// <c>Img</c> into its own internal <c>Markup</c> frame while its <c>Span</c> stayed unfolded, which
    /// shrank the unfolded root's subtree length without ever un-rooting it as an <c>Element</c> or
    /// bringing it down to exactly one frame — a bare inequality check would have missed it, and only the
    /// browser comparison caught it. Pinning the exact count is what would have caught it here, at the
    /// fast .NET layer, instead.
    /// </remarks>
    private static void AssertFoldedThenUnfolded(RenderTreeBuilder builder, int expectedUnfoldedFrameCount)
    {
        var frames = builder.GetFrames();

        var folded = frames.Array[0];
        Assert.Equal(RenderTreeFrameType.Markup, folded.FrameType);

        var unfolded = frames.Array[1];
        Assert.Equal(RenderTreeFrameType.Element, unfolded.FrameType);
        Assert.Equal(expectedUnfoldedFrameCount, unfolded.ElementSubtreeLength);

        // The folded container occupies exactly frames.Array[0] and nothing else; the unfolded
        // container's own subtree accounts for the rest. If any frame sits outside both, that is itself
        // a sign the two containers are no longer the whole of what this probe renders.
        Assert.Equal(1 + expectedUnfoldedFrameCount, frames.Count);
    }

    [Fact]
    public void TableFragment_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new TableFragmentProbe().Build(builder);
        AssertFoldedThenUnfolded(builder, expectedUnfoldedFrameCount: 8);
    }

    [Fact]
    public void SelectOptions_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new SelectOptionsProbe().Build(builder);
        AssertFoldedThenUnfolded(builder, expectedUnfoldedFrameCount: 6);
    }

    [Fact]
    public void EscapedText_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new EscapedTextProbe().Build(builder);
        AssertFoldedThenUnfolded(builder, expectedUnfoldedFrameCount: 10);
    }

    [Fact]
    public void QuotedAttribute_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new QuotedAttributeProbe().Build(builder);
        AssertFoldedThenUnfolded(builder, expectedUnfoldedFrameCount: 8);
    }

    [Fact]
    public void VoidTagInRun_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new VoidTagInRunProbe().Build(builder);
        AssertFoldedThenUnfolded(builder, expectedUnfoldedFrameCount: 7);
    }

    [Fact]
    public void MultiClass_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new MultiClassProbe().Build(builder);
        AssertFoldedThenUnfolded(builder, expectedUnfoldedFrameCount: 5);
    }
}

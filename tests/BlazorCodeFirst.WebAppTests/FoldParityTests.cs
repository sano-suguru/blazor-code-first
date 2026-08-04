using BlazorCodeFirst.WebAppTestHost.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;

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
    /// an <c>Element</c> frame whose subtree holds more than that one frame, and the two frame counts must
    /// differ.
    /// </summary>
    private static void AssertFoldedThenUnfolded(RenderTreeBuilder builder)
    {
        var frames = builder.GetFrames();
        Assert.True(frames.Count > 1, "expected both the folded and unfolded containers to contribute frames");

        var folded = frames.Array[0];
        Assert.Equal(RenderTreeFrameType.Markup, folded.FrameType);

        var unfolded = frames.Array[1];
        Assert.Equal(RenderTreeFrameType.Element, unfolded.FrameType);

        // The folded container is always exactly one frame (it is the whole of frames.Array[0]); the
        // unfolded container's frame count is its own subtree length. Asserting they differ is the
        // direct check that the unfolded spelling did not silently fold too.
        Assert.NotEqual(1, unfolded.ElementSubtreeLength);
    }

    [Fact]
    public void TableFragment_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new TableFragmentProbe().Build(builder);
        AssertFoldedThenUnfolded(builder);
    }

    [Fact]
    public void SelectOptions_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new SelectOptionsProbe().Build(builder);
        AssertFoldedThenUnfolded(builder);
    }

    [Fact]
    public void EscapedText_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new EscapedTextProbe().Build(builder);
        AssertFoldedThenUnfolded(builder);
    }

    [Fact]
    public void QuotedAttribute_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new QuotedAttributeProbe().Build(builder);
        AssertFoldedThenUnfolded(builder);
    }

    [Fact]
    public void VoidTagInRun_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new VoidTagInRunProbe().Build(builder);
        AssertFoldedThenUnfolded(builder);
    }

    [Fact]
    public void MultiClass_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new MultiClassProbe().Build(builder);
        AssertFoldedThenUnfolded(builder);
    }
}

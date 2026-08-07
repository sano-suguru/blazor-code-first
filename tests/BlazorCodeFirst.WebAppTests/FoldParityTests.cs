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

    /// <summary>
    /// <see cref="CarriageReturnProbe"/> pins a refusal rather than a fold, so it does not fit
    /// <see cref="AssertFoldedThenUnfolded"/>. Its first container is spelled entirely from constants and
    /// is foldable in every respect but one: <c>StaticMarkupSerializer.CanRoundTrip</c> excludes a
    /// carriage return, because the HTML parser normalizes CR and CRLF to LF before tokenization while
    /// <c>setAttribute</c> and <c>createTextNode</c> keep them. So the only markup frame in this probe
    /// must be the one <c>Html.Raw</c> emits deliberately.
    /// </summary>
    /// <remarks>
    /// This is the .NET-side half of three browser tests in <c>fold-parity.spec.ts</c>, which measure the
    /// divergence itself. <see cref="AssertOnlyTheExplicitRawEmitsMarkup"/> explains why the assertion
    /// counts markup frames across the whole probe instead of checking the first frame's type.
    /// </remarks>
    [Fact]
    public void CarriageReturn_is_refused_by_the_fold_so_only_the_explicit_Raw_emits_markup()
    {
        AssertOnlyTheExplicitRawEmitsMarkup(new CarriageReturnProbe().Build);
    }

    /// <summary>
    /// The other two values <c>CanRoundTrip</c> refuses, pinned the same way and for the same reason.
    /// A NUL diverges in two different shapes (dropped from text, replaced with U+FFFD in an attribute
    /// value) and a lone surrogate is expected not to diverge at all; both facts belong to
    /// <c>fold-parity.spec.ts</c>, and both are only worth measuring if the static container really did
    /// stay unfolded, which is what these two pin.
    /// </summary>
    [Fact]
    public void NullCharacter_is_refused_by_the_fold_so_only_the_explicit_Raw_emits_markup()
    {
        AssertOnlyTheExplicitRawEmitsMarkup(new NullCharacterProbe().Build);
    }

    [Fact]
    public void LoneSurrogate_is_refused_by_the_fold_so_only_the_explicit_Raw_emits_markup()
    {
        AssertOnlyTheExplicitRawEmitsMarkup(new LoneSurrogateProbe().Build);
    }

    /// <summary>
    /// <see cref="LeadingByteOrderMarkProbe"/> carries two <c>Html.Raw</c> containers rather than one,
    /// because identifying the mechanism takes both: the same value inside a markup string and at the
    /// start of one. So the count here is two, and what it pins is unchanged — that the static container
    /// contributed no markup frame of its own, which is the refusal under test.
    /// </summary>
    [Fact]
    public void LeadingByteOrderMark_is_refused_by_the_fold_so_only_the_explicit_Raws_emit_markup()
    {
        var builder = new RenderTreeBuilder();
        new LeadingByteOrderMarkProbe().Build(builder);

        var frames = builder.GetFrames();
        int markupFrames = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType == RenderTreeFrameType.Markup)
            {
                markupFrames++;
            }
        }

        Assert.Equal(2, markupFrames);
    }

    /// <summary>
    /// The classes #150's sweep found the fold may keep. This one is an ordinary folded/unfolded pair
    /// rather than a refusal, so it goes through <see cref="AssertFoldedThenUnfolded"/>: the whole point
    /// is that the static container <em>does</em> fold, since a container that quietly stopped folding
    /// would leave the browser comparing the element path against itself.
    /// </summary>
    [Fact]
    public void SweptCharacters_folded_container_folds_and_unfolded_container_does_not()
    {
        var builder = new RenderTreeBuilder();
        new SweptCharactersProbe().Build(builder);
        AssertFoldedThenUnfolded(builder, expectedUnfoldedFrameCount: 15);
    }

    /// <summary>
    /// Counts markup frames across the whole probe rather than checking the first frame's type, because
    /// each refusal probe carries its refused value inside both an attribute value and a text node:
    /// re-admitting the value would fold an inner run without necessarily changing what the first frame
    /// is. The one markup frame that must remain is the <c>Html.Raw</c> each probe spells deliberately.
    /// </summary>
    private static void AssertOnlyTheExplicitRawEmitsMarkup(System.Action<RenderTreeBuilder> build)
    {
        var builder = new RenderTreeBuilder();
        build(builder);

        var frames = builder.GetFrames();
        int markupFrames = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType == RenderTreeFrameType.Markup)
            {
                markupFrames++;
            }
        }

        Assert.Equal(1, markupFrames);
    }
}

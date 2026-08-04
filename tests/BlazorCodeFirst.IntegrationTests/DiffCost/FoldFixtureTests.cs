using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.IntegrationTests.DiffCost;

/// <summary>
/// Gates for the #140 fold measurement. The §7.1 and §7.2 gates require their two sides to render
/// equivalent frames; this pair's frames differ by construction, because that difference IS what is
/// being measured. The two conditions are therefore inverted: the pair must render the same DOM (or
/// the markup spelling is a cheaper but different workload, and the ratio measures nothing), and the
/// markup spelling must emit strictly fewer frames (or it is not folded, and the ratio measures
/// nothing either).
/// </summary>
/// <remarks>
/// The "strictly fewer frames" half of that summary no longer holds, and restating what this pair is for
/// is follow-up work under #140. Now that the emitter folds, the element spelling folds to exactly the
/// frame count the <c>Html.Raw</c> stand-in was written to predict, so the two sides are equal rather than
/// unequal. The frame assertions below say so; the DOM comparisons are unaffected.
/// </remarks>
public sealed class FoldFixtureTests : BunitContext
{
    [Fact]
    public void StaticHeavy_MarkupSpelling_RendersTheSameDomAsTheElementSpelling()
    {
        var element = Render<StaticHeavyElementView>();
        var markup = Render<StaticHeavyMarkupView>();

        markup.MarkupMatches(element.Markup);
    }

    [Fact]
    public void StaticHeavy_ElementSpelling_FoldsToThePredictedFrameCount()
    {
        var elementFrames = new RenderTreeBuilder();
        var markupFrames = new RenderTreeBuilder();
        new StaticHeavyElementView().Build(elementFrames);
        new StaticHeavyMarkupView().Build(markupFrames);

        // The element spelling now folds, so 6 is its folded count, not the 23 it emitted unfolded. Both
        // sides are asserted exactly rather than merely as equal: 6 is what the plan's frame arithmetic
        // predicts — the wrapper div, its class attribute, one markup frame for the leading static run, the
        // element and text frames of the one dynamic span, and one markup frame for the trailing run. A
        // fixture edited into a different shape therefore cannot keep passing here.
        Assert.Equal(6, elementFrames.GetFrames().Count);
        Assert.Equal(6, markupFrames.GetFrames().Count);
    }

    [Fact]
    public void Mixed_MarkupSpelling_RendersTheSameDomAsTheElementSpelling()
    {
        var element = Render<MixedElementView>();
        var markup = Render<MixedMarkupView>();

        markup.MarkupMatches(element.Markup);
    }

    [Fact]
    public void Mixed_ElementSpelling_FoldsToThePredictedFrameCount()
    {
        var elementFrames = new RenderTreeBuilder();
        var markupFrames = new RenderTreeBuilder();
        new MixedElementView().Build(elementFrames);
        new MixedMarkupView().Build(markupFrames);

        // 10 is the element spelling's folded count, not the 12 it emitted unfolded. The low-static shape:
        // only two runs are foldable and each is a single element, so the reduction is small on purpose.
        // This is the lower bound of the measured range.
        Assert.Equal(10, elementFrames.GetFrames().Count);
        Assert.Equal(10, markupFrames.GetFrames().Count);
    }
}

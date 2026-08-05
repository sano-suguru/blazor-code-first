using BlazorCodeFirst.IntegrationTests.Components;
using Bunit;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.IntegrationTests.DiffCost;

/// <summary>
/// Gates that the element spelling folds to the same frame shape as its hand-written markup counterpart.
/// Each pair was written for the #140 fold measurement, at a time when the generator did not fold: the
/// only way to spell the folded shape was to hand-write it with <c>Html.Raw</c> in the markup spelling,
/// and the element spelling was the unfolded baseline being measured against it. This pair guarantees two
/// things: the two spellings render the same DOM (or the markup spelling is a cheaper but different
/// workload, and no ratio between them means anything), and — now that the emitter folds — the element
/// spelling reaches exactly the frame count the markup spelling was written to predict.
/// </summary>
/// <remarks>
/// A regression that stops folding shows up here as the element spelling's frame count moving away from
/// the markup spelling's, back toward the pre-fold values recorded in the comments below (23 for
/// static-heavy, 12 for low-static). Those two numbers are not themselves gated: doing so would need a
/// non-folding twin with the same DOM inside this compilation, and constructing one is possible — move a
/// fixture's static text to a property reference so <c>StaticMarkupSerializer.IsFoldable</c> excludes it —
/// but is declined here. The equality assertions already fail loudly and specifically the moment folding
/// regresses, since the element spelling's count would move away from 6 or 10; a non-constant twin would
/// only re-pin the historical 23/12 endpoint, at the cost of two more fixtures whose correspondence to
/// these has to be kept in step by hand.
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

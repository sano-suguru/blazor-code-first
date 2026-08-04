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
    public void StaticHeavy_MarkupSpelling_EmitsStrictlyFewerFrames()
    {
        var elementFrames = new RenderTreeBuilder();
        var markupFrames = new RenderTreeBuilder();
        new StaticHeavyElementView().Build(elementFrames);
        new StaticHeavyMarkupView().Build(markupFrames);

        // The exact counts, not just the inequality: these are the numbers the plan's frame
        // arithmetic predicts, and a fixture edited into a different shape would otherwise keep
        // passing while measuring something else.
        Assert.Equal(23, elementFrames.GetFrames().Count);
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
    public void Mixed_MarkupSpelling_EmitsStrictlyFewerFrames()
    {
        var elementFrames = new RenderTreeBuilder();
        var markupFrames = new RenderTreeBuilder();
        new MixedElementView().Build(elementFrames);
        new MixedMarkupView().Build(markupFrames);

        // The low-static shape: only two runs are foldable and each is a single element, so the
        // reduction is small on purpose. This is the lower bound of the measured range.
        Assert.Equal(12, elementFrames.GetFrames().Count);
        Assert.Equal(10, markupFrames.GetFrames().Count);
    }
}

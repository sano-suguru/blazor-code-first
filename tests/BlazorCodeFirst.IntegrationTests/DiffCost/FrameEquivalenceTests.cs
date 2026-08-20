using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorCodeFirst.IntegrationTests.DiffCost;

public sealed class FrameEquivalenceTests
{
    [Fact]
    public void Compare_WhenFramesDifferOnlyInSequence_ReportsNoDifference()
    {
        var left = Build(b =>
        {
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "card");
            b.AddContent(2, "hi");
            b.CloseElement();
        });
        var right = Build(b =>
        {
            b.OpenElement(40, "div");
            b.AddAttribute(41, "class", "card");
            b.AddContent(42, "hi");
            b.CloseElement();
        });

        Assert.Empty(FrameEquivalence.Compare(left, right));
    }

    [Fact]
    public void Compare_WhenElementNamesDiffer_ReportsTheDifference()
    {
        var left = Build(b => { b.OpenElement(0, "div"); b.CloseElement(); });
        var right = Build(b => { b.OpenElement(0, "span"); b.CloseElement(); });

        var differences = FrameEquivalence.Compare(left, right);

        Assert.Single(differences);
        Assert.Contains("div", differences[0], StringComparison.Ordinal);
        Assert.Contains("span", differences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_WhenAttributeValuesDiffer_ReportsTheDifference()
    {
        var left = Build(b => { b.OpenElement(0, "div"); b.AddAttribute(1, "class", "a"); b.CloseElement(); });
        var right = Build(b => { b.OpenElement(0, "div"); b.AddAttribute(1, "class", "b"); b.CloseElement(); });

        Assert.Single(FrameEquivalence.Compare(left, right));
    }

    [Fact]
    public void Compare_WhenKeysDiffer_ReportsTheDifference()
    {
        var left = Build(b => { b.OpenElement(0, "div"); b.SetKey("k1"); b.CloseElement(); });
        var right = Build(b => { b.OpenElement(0, "div"); b.SetKey("k2"); b.CloseElement(); });

        Assert.Single(FrameEquivalence.Compare(left, right));
    }

    [Fact]
    public void Compare_WhenFrameCountsDiffer_ReportsTheLengthMismatch()
    {
        var left = Build(b => { b.OpenElement(0, "div"); b.AddContent(1, "hi"); b.CloseElement(); });
        var right = Build(b => { b.OpenElement(0, "div"); b.CloseElement(); });

        var differences = FrameEquivalence.Compare(left, right);

        Assert.Single(differences);
        Assert.Contains("frame count", differences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_WhenIgnoringRegionsAndOnlyRegionsDiffer_ReportsNoDifference()
    {
        // The A-vs-C gate: A wraps its children in a region, C does not. Removing A's region frame
        // must also reduce the enclosing div's subtree length, or the nesting would appear to differ.
        var withRegion = Build(b =>
        {
            b.OpenElement(0, "div");
            b.OpenRegion(1);
            b.AddContent(2, "hi");
            b.CloseRegion();
            b.CloseElement();
        });
        var withoutRegion = Build(b =>
        {
            b.OpenElement(0, "div");
            b.AddContent(1, "hi");
            b.CloseElement();
        });

        Assert.Empty(FrameEquivalence.Compare(withRegion, withoutRegion, ignoreRegionFrames: true));
    }

    [Fact]
    public void Compare_WhenIgnoringRegionsAndContentAlsoDiffers_StillReportsTheDifference()
    {
        var withRegion = Build(b =>
        {
            b.OpenElement(0, "div");
            b.OpenRegion(1);
            b.AddContent(2, "hi");
            b.CloseRegion();
            b.CloseElement();
        });
        var withoutRegion = Build(b =>
        {
            b.OpenElement(0, "div");
            b.AddContent(1, "bye");
            b.CloseElement();
        });

        Assert.Single(FrameEquivalence.Compare(withRegion, withoutRegion, ignoreRegionFrames: true));
    }

    [Fact]
    public void AssertEquivalent_WhenFramesDiffer_ThrowsListingTheDifferences()
    {
        var left = Build(b => { b.OpenElement(0, "div"); b.CloseElement(); });
        var right = Build(b => { b.OpenElement(0, "span"); b.CloseElement(); });

        var exception = Assert.Throws<InvalidOperationException>(
            () => FrameEquivalence.AssertEquivalent(left, right));

        Assert.Contains("span", exception.Message, StringComparison.Ordinal);
    }

    private static RenderTreeBuilder Build(Action<RenderTreeBuilder> build)
    {
        var builder = new RenderTreeBuilder();
        build(builder);
        return builder;
    }
}

using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.WebAppTests;

/// <summary>
/// What an attribute splat costs and buys, which is the premise under <c>ARCHITECTURE.md</c> Appendix B.14
/// and the §4.1 rule it carries: a tag or an attribute <em>name</em> is a token the generator reads, never a
/// value (#308/#320), but a dictionary of already-resolved name+value pairs is a narrower thing, adopted for
/// <c>ElementView.Attrs</c> on the emission order this file measures below (#387). The subject throughout is
/// <see cref="RenderTreeBuilder"/> itself, not this surface's generated output (that is
/// <c>RenderViewEmitterDecorationTests</c>' job), and pinning it against the referenced package is what keeps
/// the decision resting on the platform's behaviour rather than on a reading of its source.
/// </summary>
/// <remarks>
/// #308 assumed the obstacle was sequencing, because the attribute count is a runtime value. It is not:
/// <see cref="RenderTreeBuilder.AddMultipleAttributes"/> takes one sequence number for the whole
/// dictionary. What the splat actually costs is the class channel and BCF3010 — a splat makes the builder
/// resolve a duplicate name silently, last frame winning, where the same duplicate written out survives as
/// two frames. And an attribute cannot follow a region, so the region path #17 landed reaches elements and
/// content but not the attribute channel.
/// </remarks>
public sealed class AttributeSplatMeasurementTests
{
    [Fact]
    public void Splat_ConsumesOneSequenceNumber_WhateverTheDictionaryHolds()
    {
        using var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, new Dictionary<string, object>
        {
            ["data-a"] = "1",
            ["data-b"] = "2",
            ["data-c"] = "3",
        });
        builder.AddContent(2, "child");
        builder.CloseElement();

        var frames = Frames(builder);
        var attributes = frames.FindAll(f => f.FrameType == RenderTreeFrameType.Attribute);

        Assert.Equal(3, attributes.Count);
        Assert.All(attributes, f => Assert.Equal(1, f.Sequence));

        // The number after the splat is unmoved by the count, which is what static sequencing asks for:
        // the width to reserve is one, not the dictionary's size.
        Assert.Equal(2, Assert.Single(frames, f => f.FrameType == RenderTreeFrameType.Text).Sequence);
    }

    [Fact]
    public void Splat_ResolvesADuplicateNameSilently_TheLastFrameWinning()
    {
        using var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "card");
        builder.AddMultipleAttributes(2, new Dictionary<string, object> { ["class"] = "from-dictionary" });
        builder.CloseElement();

        var attribute = Assert.Single(Frames(builder), f => f.FrameType == RenderTreeFrameType.Attribute);

        // The earlier frame is gone rather than joined to. A class channel folded at compile time would be
        // replaced by the dictionary's value, not extended by it, and the replacement is chosen by a key
        // that appears nowhere in the source.
        Assert.Equal("class", attribute.AttributeName);
        Assert.Equal("from-dictionary", attribute.AttributeValue);
    }

    [Fact]
    public void SplatFirst_TheExplicitFrameWins_AndUnwrittenKeysSurvive()
    {
        using var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, new Dictionary<string, object>
        {
            ["class"] = "from-dictionary",
            ["id"] = "from-dictionary",
        });
        builder.AddAttribute(2, "class", "card");
        builder.CloseElement();

        var attributes = Frames(builder).FindAll(f => f.FrameType == RenderTreeFrameType.Attribute);

        // Reversing the order from the test above flips the outcome: the explicit frame now wins
        // (the folded class channel's value survives, unreplaced), and the dictionary key the call
        // site never wrote ("id") survives untouched beside it. This is the #387 decision.
        Assert.Equal(2, attributes.Count);
        Assert.Contains(attributes, a => a.AttributeName == "class" && (string)a.AttributeValue! == "card");
        Assert.Contains(attributes, a => a.AttributeName == "id" && (string)a.AttributeValue! == "from-dictionary");
    }

    [Fact]
    public void Splat_WithANullDictionary_AppendsNoFrameAndDoesNotThrow()
    {
        // Backs the decision to let `.Attrs` accept a null dictionary rather than rejecting it: the
        // platform itself treats a null AddMultipleAttributes argument as "nothing to add", not an
        // error, so the generated code needs no null guard of its own.
        using var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        var ex = Record.Exception(() =>
            builder.AddMultipleAttributes(1, (IEnumerable<KeyValuePair<string, object>>?)null));
        builder.AddAttribute(2, "class", "card");
        builder.CloseElement();

        Assert.Null(ex);
        // The null-dictionary call appended no frame, so the one remaining attribute frame is the
        // explicit .Attr("class", …) at sequence 2, not the splat's own sequence 1 — that sequence
        // number is simply never used for a frame when the dictionary is null.
        var attribute = Assert.Single(Frames(builder), f => f.FrameType == RenderTreeFrameType.Attribute);
        Assert.Equal(2, attribute.Sequence);
        Assert.Equal("card", (string)attribute.AttributeValue!);
    }

    [Fact]
    public void WithoutASplat_TheSameDuplicateSurvivesAsTwoFrames()
    {
        using var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "card");
        builder.AddAttribute(2, "class", "from-the-second-decoration");
        builder.CloseElement();

        var attributes = Frames(builder).FindAll(f => f.FrameType == RenderTreeFrameType.Attribute);

        // Deduplication runs only for an element a splat touched. This is the output BCF3010 refuses, and
        // the asymmetry is why that rule would become a rule about how the name happened to be spelled.
        Assert.Equal(2, attributes.Count);
        Assert.All(attributes, f => Assert.Equal("class", f.AttributeName));
    }

    [Fact]
    public void AnAttributeCannotFollowARegion()
    {
        using var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        builder.OpenRegion(1);

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddAttribute(2, "class", "card"));

        Assert.Contains("Attributes may only be added immediately after frames", error.Message, StringComparison.Ordinal);
    }

    /// <summary>The frames appended so far, in order.</summary>
    private static List<RenderTreeFrame> Frames(RenderTreeBuilder builder)
    {
        var range = builder.GetFrames();
        return [.. range.Array.Take(range.Count)];
    }
}

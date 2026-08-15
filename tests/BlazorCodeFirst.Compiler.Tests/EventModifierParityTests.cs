#pragma warning disable BL0006 // Reading render-tree frames is what a compiler-contract test is for:
                               // CONTRIBUTING.md §Engineering standard names generated source and sequence
                               // numbers as the architectural contract compiler tests may reach into.
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The generator writes the modifier attribute's name itself rather than calling
/// <c>AddEventPreventDefaultAttribute</c>, so the shipped package keeps the granular
/// <c>Microsoft.AspNetCore.Components</c> reference #23 landed and #156 stays undecided. That trade is
/// only safe while the two spellings produce the same frames, and nothing in the framework promises they
/// will: the name is internal to Blazor's renderer.
/// </summary>
/// <remarks>
/// This is where a framework change to the naming scheme fails a test instead of shipping a silent no-op.
/// Both sides read <see cref="RenderViewEmitter"/>'s own constants rather than transcribing the literal,
/// so a typo in the emitter cannot hide behind a matching typo here (#368).
/// </remarks>
public sealed class EventModifierParityTests
{
    [Fact]
    public void PreventDefaultTrue_GeneratedSpelling_MatchesTheFrameworkExtensionFrameForFrame() =>
        Assert.Equal(
            Describe(builder => builder.AddEventPreventDefaultAttribute(2, "onwheel", true)),
            Describe(builder => builder.AddAttribute(
                2, RenderViewEmitter.PreventDefaultAttributePrefix + "onwheel", true)));

    [Fact]
    public void StopPropagationTrue_GeneratedSpelling_MatchesTheFrameworkExtensionFrameForFrame() =>
        Assert.Equal(
            Describe(builder => builder.AddEventStopPropagationAttribute(2, "onwheel", true)),
            Describe(builder => builder.AddAttribute(
                2, RenderViewEmitter.StopPropagationAttributePrefix + "onwheel", true)));

    /// <summary>
    /// A <see langword="false"/> appends no frame on either spelling, asserted as an absence.
    /// </summary>
    /// <remarks>
    /// Deliberately not an equality against the extension: both sides append nothing, so comparing them
    /// would hold for any attribute name whatsoever and would keep holding after a typo. What is worth
    /// pinning here is that the emitted call really does drop out of the frames, which is the premise the
    /// sequence-number rule rests on.
    /// </remarks>
    [Fact]
    public void False_AppendsNoFrameOnEitherSpelling()
    {
        var viaExtension = Describe(builder => builder.AddEventPreventDefaultAttribute(2, "onwheel", false));
        var viaGenerator = Describe(builder => builder.AddAttribute(
            2, RenderViewEmitter.PreventDefaultAttributePrefix + "onwheel", false));

        Assert.Equal(viaExtension, viaGenerator);
        Assert.DoesNotContain(
            viaGenerator,
            frame => frame.Contains("__internal_", StringComparison.Ordinal));
    }

    /// <summary>
    /// The frames of a div carrying an <c>onwheel</c> handler plus whatever <paramref name="write"/> adds,
    /// as comparable text.
    /// </summary>
    private static string[] Describe(Action<RenderTreeBuilder> write)
    {
        using var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        builder.AddAttribute(
            1, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(new object(), _ => { }));
        write(builder);
        builder.CloseElement();

        var range = builder.GetFrames();
        var described = new List<string>(range.Count);
        for (var i = 0; i < range.Count; i++)
        {
            var frame = range.Array[i];
            described.Add(
                $"{frame.FrameType}|{frame.Sequence}|" +
                (frame.FrameType == RenderTreeFrameType.Attribute
                    ? $"{frame.AttributeName}={frame.AttributeValue}"
                    : string.Empty));
        }

        return [.. described];
    }
}
#pragma warning restore BL0006

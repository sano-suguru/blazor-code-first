using System;
using BlazorCodeFirst.IntegrationTests.DiffCost;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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
/// so a typo in the emitter cannot hide behind a matching typo here. The comparison itself is
/// <see cref="FrameEquivalence"/>, the same gate the diff-cost harness uses, so a mismatch names the
/// differing frame rather than reporting that two string lists differ (#368).
/// </remarks>
public sealed class EventModifierParityTests
{
    [Fact]
    public void PreventDefaultTrue_GeneratedSpelling_MatchesTheFrameworkExtension() =>
        AssertSameFrames(
            builder => builder.AddEventPreventDefaultAttribute(2, "onwheel", true),
            builder => builder.AddAttribute(
                2, RenderViewEmitter.PreventDefaultAttributePrefix + "onwheel", true));

    [Fact]
    public void StopPropagationTrue_GeneratedSpelling_MatchesTheFrameworkExtension() =>
        AssertSameFrames(
            builder => builder.AddEventStopPropagationAttribute(2, "onwheel", true),
            builder => builder.AddAttribute(
                2, RenderViewEmitter.StopPropagationAttributePrefix + "onwheel", true));

    /// <summary>
    /// A <see langword="false"/> appends no frame, asserted against a builder that writes nothing.
    /// </summary>
    /// <remarks>
    /// Comparing the two spellings against each other would hold here for any attribute name whatsoever,
    /// since both append nothing, and would keep holding after a typo. Comparing against the empty
    /// spelling states the premise the sequence-number rule actually rests on: the emitted call drops out
    /// of the frames while its number stays spent.
    /// </remarks>
    [Fact]
    public void False_AppendsNoFrameOnEitherSpelling()
    {
        AssertSameFrames(
            _ => { },
            builder => builder.AddAttribute(
                2, RenderViewEmitter.PreventDefaultAttributePrefix + "onwheel", false));

        AssertSameFrames(
            _ => { },
            builder => builder.AddEventPreventDefaultAttribute(2, "onwheel", false));
    }

    /// <summary>
    /// Builds a div carrying an <c>onwheel</c> handler plus whatever each writer adds, and compares the
    /// two frame lists on everything except sequence numbers.
    /// </summary>
    private static void AssertSameFrames(Action<RenderTreeBuilder> expected, Action<RenderTreeBuilder> actual)
    {
        using var expectedBuilder = Build(expected);
        using var actualBuilder = Build(actual);
        FrameEquivalence.AssertEquivalent(expectedBuilder, actualBuilder);
    }

    private static RenderTreeBuilder Build(Action<RenderTreeBuilder> write)
    {
        var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "div");
        builder.AddAttribute(
            1, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(new object(), _ => { }));
        write(builder);
        builder.CloseElement();
        return builder;
    }
}

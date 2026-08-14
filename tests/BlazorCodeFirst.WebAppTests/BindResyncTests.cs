using BlazorCodeFirst.WebAppTestHost.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.WebAppTests;

/// <summary>
/// Pins, on the .NET side, the premises <c>bind-resync.spec.ts</c> depends on. For
/// <see cref="TrimmingInputProbe"/>: that it really does carry <c>SetUpdatesAttributeName("value")</c>
/// and really does normalize the value on the way in. For <see cref="TwoBindingProbe"/>: that a second
/// binding really is present on the element, and that it is still the <c>value</c> binding — and only
/// that one — which is registered for resynchronization. This is the same two-part arrangement
/// <c>FoldParityTests</c> has with <c>fold-parity.spec.ts</c>, and for the same reason — a green browser
/// run means nothing if the premise underneath it has flipped, and the browser suite cannot tell the
/// difference between "the resynchronization works" and "there was nothing to resynchronize". CI runs
/// both halves; that is what makes the pair worth having rather than either one alone.
/// No assertion here can replace the browser test: the resynchronization these facts enable is a
/// property of Blazor's JS renderer and is invisible from every .NET test layer in this repository.
/// </summary>
public sealed class BindResyncTests
{
    [Fact]
    public void TrimmingInputProbe_marks_the_value_attribute_for_DOM_resynchronization()
    {
        var builder = new RenderTreeBuilder();
        new TrimmingInputProbe().Build(builder);

        var resynchronized = ResynchronizedAttributes(builder);

        // Exactly one, and named "value": the browser test types into one input and reads back that one
        // attribute. A second binding appearing on this page, or the name drifting to something the
        // element does not display, would leave the browser measuring nothing while still passing.
        Assert.Equal(["value"], resynchronized);
    }

    [Fact]
    public void TrimmingInputProbe_binds_an_event_the_browser_actually_raises_while_typing()
    {
        var builder = new RenderTreeBuilder();
        new TrimmingInputProbe().Build(builder);

        var frames = builder.GetFrames();
        var eventNames = new List<string>();
        for (int i = 0; i < frames.Count; i++)
        {
            ref readonly var frame = ref frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Attribute
                && frame.AttributeEventUpdatesAttributeName is not null)
            {
                eventNames.Add(frame.AttributeName);
            }
        }

        // "oninput" and not "onchange". The browser test's second interaction never blurs the field, so
        // an onchange binding would raise no event at all, and the test would report a resync failure
        // that was really a missing round trip.
        Assert.Equal(["oninput"], eventNames);
    }

    [Fact]
    public void TwoBindingProbe_marks_only_the_value_attribute_for_DOM_resynchronization()
    {
        var builder = new RenderTreeBuilder();
        new TwoBindingProbe().Build(builder);

        var resynchronized = ResynchronizedAttributes(builder);

        // Exactly one, and named "value". The second binding names "data-committed", which the client
        // can never send back — EventFieldInfo carries the element's own value or checked and nothing
        // else — so the emitter records nothing for it. If this became two, the browser test below would
        // be measuring a page whose retained render tree gets corrupted on every change event.
        Assert.Equal(["value"], resynchronized);
    }

    [Fact]
    public void TwoBindingProbe_emits_the_frames_for_both_bindings()
    {
        var builder = new RenderTreeBuilder();
        new TwoBindingProbe().Build(builder);

        var frames = builder.GetFrames();
        var attributeNames = new List<string>();
        for (int i = 0; i < frames.Count; i++)
        {
            ref readonly var frame = ref frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Attribute)
            {
                attributeNames.Add(frame.AttributeName);
            }
        }

        // The point of the browser test is that a second binding is genuinely present. If either of
        // these went missing the run below would pass while measuring the single-binding case.
        Assert.Contains("oninput", attributeNames);
        Assert.Contains("onchange", attributeNames);
        Assert.Contains("data-committed", attributeNames);
    }

    [Fact]
    public void RejectingInputProbe_marks_the_value_attribute_for_DOM_resynchronization()
    {
        var builder = new RenderTreeBuilder();
        new RejectingInputProbe().Build(builder);

        var resynchronized = ResynchronizedAttributes(builder);

        // The same premise the trimming probe needs, for the other door into the same repair: without
        // this the browser test would be measuring an element Blazor never repairs, and would report a
        // reversion failure that was really a missing registration.
        Assert.Equal(["value"], resynchronized);
    }

    [Fact]
    public void RejectingInputProbe_formats_its_value_through_the_written_culture()
    {
        var builder = new RenderTreeBuilder();
        new RejectingInputProbe().Build(builder);

        var frames = builder.GetFrames();
        string? value = null;
        for (int i = 0; i < frames.Count; i++)
        {
            ref readonly var frame = ref frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "value")
            {
                value = frame.AttributeValue as string;
            }
        }

        // The frame holds a formatted string, which is what the browser test reads back after the
        // reversion. A frame holding a boxed int would still render "30" and would still pass in the
        // browser, while meaning the emitter had stopped going through BindConverter (#307).
        Assert.Equal("30", value);
    }

    /// <summary>
    /// The attribute names a builder's frames register for DOM resynchronization, in order.
    /// </summary>
    /// <remarks>
    /// Extracted when the third probe arrived. Every premise in this file is the same question asked of a
    /// different component, and the loop is the question: what did <c>SetUpdatesAttributeName</c> record.
    /// Keeping one copy is also what makes each test read as the one thing it asserts, which is the count
    /// and the name rather than how a frame carries them.
    /// </remarks>
    private static List<string> ResynchronizedAttributes(RenderTreeBuilder builder)
    {
        var frames = builder.GetFrames();
        var resynchronized = new List<string>();
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].AttributeEventUpdatesAttributeName is { } name)
            {
                resynchronized.Add(name);
            }
        }

        return resynchronized;
    }
}

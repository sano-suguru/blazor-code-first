using BlazorCodeFirst.WebAppTestHost.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.WebAppTests;

/// <summary>
/// Pins, on the .NET side, the premise <c>bind-resync.spec.ts</c> depends on: that
/// <see cref="TrimmingInputProbe"/> really does carry <c>SetUpdatesAttributeName("value")</c> and really
/// does normalize the value on the way in. This is the same two-part arrangement
/// <c>FoldParityTests</c> has with <c>fold-parity.spec.ts</c>, and for the same reason — the browser
/// suite is not run by <c>dotnet test</c> or by CI, so if its premise silently flipped nothing would say
/// so. Neither assertion here can replace the browser test: the resynchronization these two facts enable
/// is a property of Blazor's JS renderer and is invisible from every .NET test layer in this repository.
/// </summary>
public sealed class BindResyncTests
{
    [Fact]
    public void TrimmingInputProbe_marks_the_value_attribute_for_DOM_resynchronization()
    {
        var builder = new RenderTreeBuilder();
        new TrimmingInputProbe().Build(builder);

        var frames = builder.GetFrames();
        var resynchronized = new List<string>();
        for (int i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].AttributeEventUpdatesAttributeName is { } name)
            {
                resynchronized.Add(name);
            }
        }

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
}

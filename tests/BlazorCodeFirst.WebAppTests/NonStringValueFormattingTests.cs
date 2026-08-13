using System.Globalization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.WebAppTests;

/// <summary>
/// Where a non-string value is formatted, which is the premise under both halves of the surface's value
/// typing: <c>DESIGN.md</c> §4.1 refuses <c>object?</c> attribute values (#158) and refuses a numeric
/// child spelling (#245), and <c>ARCHITECTURE.md</c> §2.7(D) refuses to fold non-string constants. All
/// three rest on the compiler being unable to know which culture applies. The reason it cannot is pinned
/// here: formatting happens inside the <c>AddContent</c> / <c>AddAttribute</c> call and follows the
/// culture of the thread that made it, which is the thread running <c>RenderView</c>.
/// </summary>
/// <remarks>
/// #158's rationale used to say the value was formatted later, by whichever thread formatted it. That is
/// not what happens — the frame already holds a string — and the distinction matters, because #245 turns
/// on whether a numeric child would format anywhere other than an interpolated string does. It would not.
/// </remarks>
public sealed class NonStringValueFormattingTests
{
    /// <summary>Runs <paramref name="action"/> on a fresh thread whose culture is <paramref name="culture"/>.</summary>
    private static void OnThreadWithCulture(string culture, Action action)
    {
        var thread = new Thread(() =>
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            action();
        });

        thread.Start();
        thread.Join();
    }

    /// <summary>The text of every <see cref="RenderTreeFrameType.Text"/> frame, in order.</summary>
    private static List<string?> TextFrames(RenderTreeBuilder builder)
    {
        var frames = builder.GetFrames();
        var texts = new List<string?>();
        for (var index = 0; index < frames.Count; index++)
        {
            if (frames.Array[index].FrameType == RenderTreeFrameType.Text)
                texts.Add(frames.Array[index].TextContent);
        }

        return texts;
    }

    /// <summary>
    /// A boxed value handed to <c>AddContent</c> is formatted at the call, not kept for later: the two
    /// frames disagree even though they sit in one builder, and they were separated by nothing but the
    /// ambient culture at the moment each call ran.
    /// </summary>
    [Fact]
    public void AddContent_WithABoxedValue_FormatsAtTheCall()
    {
        var builder = new RenderTreeBuilder();

        OnThreadWithCulture("de-DE", () => builder.AddContent(0, (object)1234.5d));
        OnThreadWithCulture("en-US", () => builder.AddContent(1, (object)1234.5d));

        Assert.Equal(["1234,5", "1234.5"], TextFrames(builder));
    }

    /// <summary>
    /// The interpolated-string spelling the surface offers today formats in exactly the same place, which
    /// is the whole of #245's finding: the two spellings of a number are indistinguishable in when, where,
    /// and under which culture they format.
    /// </summary>
    [Fact]
    public void AddContent_WithAnInterpolatedString_FormatsAtTheSamePlace()
    {
        var builder = new RenderTreeBuilder();

        OnThreadWithCulture("de-DE", () => builder.AddContent(0, $"{1234.5d}"));
        OnThreadWithCulture("en-US", () => builder.AddContent(1, $"{1234.5d}"));

        Assert.Equal(["1234,5", "1234.5"], TextFrames(builder));
    }

    /// <summary>
    /// Reading the frames under a third culture changes neither. Formatting is finished by the time the
    /// frame exists, so the renderer has no value left to format and no culture of its own to apply.
    /// </summary>
    [Fact]
    public void Frames_ReadUnderADifferentCulture_AreUnchanged()
    {
        var builder = new RenderTreeBuilder();
        OnThreadWithCulture("de-DE", () => builder.AddContent(0, (object)1234.5d));

        List<string?>? read = null;
        OnThreadWithCulture("en-US", () => read = TextFrames(builder));

        Assert.Equal(["1234,5"], read);
    }

    /// <summary>
    /// The attribute channel #158 decided does the same thing, on the element path: the frame's value is
    /// already a <see cref="string"/>. This is the measurement that corrects #158's stated mechanism while
    /// leaving its decision standing — the culture is still one the call site cannot see, because the call
    /// site does not choose the thread <c>RenderView</c> runs on.
    /// </summary>
    [Fact]
    public void AddAttribute_OnAnElement_FormatsAtTheCall()
    {
        var builder = new RenderTreeBuilder();

        OnThreadWithCulture("de-DE", () =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "data-x", (object)1234.5d);
            builder.CloseElement();
        });

        var frames = builder.GetFrames();
        object? value = null;
        for (var index = 0; index < frames.Count; index++)
        {
            if (frames.Array[index].FrameType == RenderTreeFrameType.Attribute)
                value = frames.Array[index].AttributeValue;
        }

        Assert.Equal("1234,5", Assert.IsType<string>(value));
    }
}

using System.Globalization;
using System.Runtime.ExceptionServices;
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
    /// <summary>
    /// Runs <paramref name="action"/> on a fresh thread whose culture is <paramref name="culture"/>. A
    /// dedicated thread rather than setting and restoring the culture here: these tests exist to show that
    /// the culture is the calling thread's, so borrowing xunit's would leave the isolation resting on the
    /// restore rather than on the thread boundary. Anything the action throws is rethrown after the join,
    /// because an escaping exception on a foreground thread ends the process instead of the test.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The catch marshals the failure across a thread boundary rather than swallowing it: " +
            "ExceptionDispatchInfo rethrows it on the caller after the join, with its original stack. " +
            "Narrowing the type would let the excluded types escape the foreground thread, which ends the " +
            "test process instead of failing the test — the very failure mode this exists to prevent.")]
    private static void OnThreadWithCulture(string culture, Action action)
    {
        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });

        thread.Start();
        thread.Join();
        failure?.Throw();
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

    /// <summary>The value of every <see cref="RenderTreeFrameType.Attribute"/> frame, in order.</summary>
    /// <remarks>
    /// The attribute sibling of <see cref="TextFrames"/>, extracted when the second caller arrived: both
    /// attribute measurements below read the same four lines, and the whole point of the pair is that one
    /// holds a value the thread's culture chose and the other one it did not.
    /// </remarks>
    private static List<object?> AttributeValues(RenderTreeBuilder builder)
    {
        var frames = builder.GetFrames();
        var values = new List<object?>();
        for (var index = 0; index < frames.Count; index++)
        {
            if (frames.Array[index].FrameType == RenderTreeFrameType.Attribute)
                values.Add(frames.Array[index].AttributeValue);
        }

        return values;
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

        var read = new List<string?>();
        OnThreadWithCulture("en-US", () => read.AddRange(TextFrames(builder)));

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

        Assert.Equal("1234,5", Assert.IsType<string>(Assert.Single(AttributeValues(builder))));
    }

    /// <summary>
    /// The same call under a written culture, which is what #307 changed and the reason #158's decision
    /// no longer reaches the element-side <c>.Bind</c>. Every measurement above turns on the culture being
    /// the calling thread's; here the thread still carries de-DE and the frame holds the invariant
    /// spelling, because the generated code names the culture instead of letting the thread supply it.
    /// </summary>
    /// <remarks>
    /// Written as the call the generator emits rather than by rendering a component: the point being
    /// measured is which culture <c>FormatValue</c> uses, and a component would put the generator's own
    /// arithmetic between the claim and the evidence. The emitted spelling is asserted by
    /// <c>HtmlBindGeneratorTests</c>, and the round trip by <c>BindRenderingTests</c>; this is the middle
    /// fact neither of those can see.
    /// </remarks>
    [Fact]
    public void AddAttribute_ThroughBindConverterWithAWrittenCulture_IgnoresTheThreadsCulture()
    {
        var builder = new RenderTreeBuilder();

        OnThreadWithCulture("de-DE", () =>
        {
            builder.OpenElement(0, "input");
            builder.AddAttribute(
                1,
                "value",
                Microsoft.AspNetCore.Components.BindConverter.FormatValue(
                    1234.5m, culture: CultureInfo.InvariantCulture));
            builder.CloseElement();
        });

        Assert.Equal("1234.5", Assert.IsType<string>(Assert.Single(AttributeValues(builder))));
    }
}

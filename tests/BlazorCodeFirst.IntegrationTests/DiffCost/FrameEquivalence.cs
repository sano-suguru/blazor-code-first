using System.Globalization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace BlazorCodeFirst.IntegrationTests.DiffCost;

/// <summary>
/// Compares two render-tree frame lists on everything except <c>Sequence</c>.
/// </summary>
/// <remarks>
/// This is the gate that makes a measurement attributable. DESIGN.md §7.1 compares a BlazorCodeFirst
/// component against an equivalent Razor component, and §7.2 compares static sequence assignment
/// against runtime increment. In both cases the number means nothing unless the two sides emit the
/// same frames, so a mismatch in element count, attribute value, or key would silently land in the
/// published figure. Sequence numbers are the one thing allowed to differ, because they are the
/// property under test in §7.2 and because the two sources differ textually in §7.1.
/// <para>
/// Nesting in a frame list is encoded solely in the subtree lengths, so those are compared too.
/// <c>ignoreRegionFrames</c> supports comparing against a variant that emits no regions at all: the
/// region frames are dropped and every surviving frame's subtree length is reduced by the number of
/// region frames inside its span, so both sides describe the same nesting.
/// </para>
/// </remarks>
public static class FrameEquivalence
{
    /// <summary>
    /// Returns one entry per difference; an empty list means the two builders are equivalent.
    /// </summary>
    public static IReadOnlyList<string> Compare(
        RenderTreeBuilder expected,
        RenderTreeBuilder actual,
        bool ignoreRegionFrames = false)
    {
        var left = Describe(expected, ignoreRegionFrames);
        var right = Describe(actual, ignoreRegionFrames);

        if (left.Count != right.Count)
        {
            return
            [
                $"frame count differs: expected {left.Count}, actual {right.Count}" +
                $"{Environment.NewLine}expected: {string.Join(" | ", left)}" +
                $"{Environment.NewLine}actual:   {string.Join(" | ", right)}",
            ];
        }

        var differences = new List<string>();
        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
            {
                differences.Add($"frame {i}: expected {left[i]}, actual {right[i]}");
            }
        }

        return differences;
    }

    /// <summary>Throws when the two builders are not equivalent, naming every difference.</summary>
    public static void AssertEquivalent(
        RenderTreeBuilder expected,
        RenderTreeBuilder actual,
        bool ignoreRegionFrames = false)
    {
        var differences = Compare(expected, actual, ignoreRegionFrames);
        if (differences.Count > 0)
        {
            throw new InvalidOperationException(
                "Frame lists are not equivalent, so any measurement comparing them would be " +
                "attributable to a structural mismatch rather than to sequence assignment:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, differences));
        }
    }

    private static List<string> Describe(RenderTreeBuilder builder, bool ignoreRegionFrames)
    {
        var frames = builder.GetFrames();
        var result = new List<string>(frames.Count);

        for (int i = 0; i < frames.Count; i++)
        {
            ref var frame = ref frames.Array[i];

            if (ignoreRegionFrames && frame.FrameType == RenderTreeFrameType.Region)
            {
                continue;
            }

            int subtreeLength = SubtreeLength(ref frame);
            if (ignoreRegionFrames)
            {
                subtreeLength -= CountRegionFrames(frames, i + 1, i + subtreeLength);
            }

            result.Add(Describe(ref frame, subtreeLength));
        }

        return result;
    }

    private static int CountRegionFrames(ArrayRange<RenderTreeFrame> frames, int start, int endExclusive)
    {
        int count = 0;
        for (int i = start; i < endExclusive && i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType == RenderTreeFrameType.Region)
            {
                count++;
            }
        }

        return count;
    }

    private static int SubtreeLength(ref RenderTreeFrame frame) => frame.FrameType switch
    {
        RenderTreeFrameType.Element => frame.ElementSubtreeLength,
        RenderTreeFrameType.Component => frame.ComponentSubtreeLength,
        RenderTreeFrameType.Region => frame.RegionSubtreeLength,
        _ => 1,
    };

    private static string Describe(ref RenderTreeFrame frame, int subtreeLength) => frame.FrameType switch
    {
        RenderTreeFrameType.Element =>
            $"Element({frame.ElementName}) key={Key(frame.ElementKey)} subtree={subtreeLength}",
        RenderTreeFrameType.Component =>
            $"Component({frame.ComponentType}) key={Key(frame.ComponentKey)} subtree={subtreeLength}",
        RenderTreeFrameType.Region => $"Region subtree={subtreeLength}",
        RenderTreeFrameType.Text => $"Text({frame.TextContent})",
        RenderTreeFrameType.Markup => $"Markup({frame.MarkupContent})",
        RenderTreeFrameType.Attribute =>
            $"Attribute({frame.AttributeName})={Value(frame.AttributeValue)}",

        // Every frame type this harness's components can emit is handled above. A new type reaching
        // here would otherwise compare equal on both sides and silently weaken the gate, so name it
        // rather than fall back to something uninformative.
        _ => $"Unhandled({frame.FrameType}) subtree={subtreeLength}",
    };

    private static string Key(object? key) =>
        key is null ? "none" : Convert.ToString(key, CultureInfo.InvariantCulture) ?? "none";

    /// <summary>
    /// Renders an attribute value for comparison. Strings and primitives compare by value. Delegates
    /// (event callbacks) are distinct instances on both sides even when semantically identical, so
    /// only their type is compared; an event's presence, name, and position are still checked.
    /// </summary>
    private static string Value(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        Delegate => "<delegate>",
        _ => $"<{value.GetType().Name}>",
    };
}

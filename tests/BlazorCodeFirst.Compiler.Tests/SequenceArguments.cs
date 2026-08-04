using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Reads the sequence arguments out of a generated <c>RenderView</c> body.
/// </summary>
/// <remarks>
/// <para>
/// One extractor for every test that checks sequence numbers. The per-file regexes this replaces
/// disagreed about which calls consume a sequence: the one in <c>RenderViewEmitterDecorationTests</c>
/// omitted both <c>OpenRegion</c> and <c>AddMarkupContent</c>, and the one in
/// <c>RenderViewEmitterRawFragmentTests</c> omitted <c>OpenRegion</c>, so a drift in a construct that
/// file happened not to exercise went unseen.
/// </para>
/// <para>
/// <c>CloseElement</c>, <c>CloseRegion</c>, <c>CloseComponent</c>, and <c>SetKey</c> consume no sequence
/// number and are deliberately absent from the pattern.
/// </para>
/// </remarks>
internal static partial class SequenceArguments
{
    // OpenComponent needs its own alternative because the type argument sits between the method name and
    // the paren. The type is matched lazily up to ">(" rather than with a negated character class, so a
    // generic component type (OpenComponent<global::Foo<Bar>>) still matches: the lazy run extends past
    // the inner '>' because only the final one is followed by '('.
    [GeneratedRegex(
        @"__builder\.(?:OpenElement|OpenRegion|AddAttribute|AddContent|AddMarkupContent|AddComponentParameter)\((\d+)"
            + @"|__builder\.OpenComponent<.+?>\((\d+)")]
    private static partial Regex SequenceConsumingCall();

    /// <summary>
    /// The sequence argument of every sequence-consuming builder call, in order of textual appearance.
    /// </summary>
    public static IReadOnlyList<int> InTextOrder(string generatedCode) =>
    [
        .. SequenceConsumingCall().Matches(generatedCode)
            .Select(static match => int.Parse(
                match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)),
    ];

    /// <summary>
    /// Asserts the emitted sequence arguments are exactly <c>0..N-1</c> in textual appearance order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Density is a property of this allocator, not a Blazor requirement: Blazor requires only that a
    /// sequence number be stable for a source position. Every node kind writes into the emitted text every
    /// number it reserves, so a hole or a repeat means the emitter's cursor and its reservation disagree.
    /// That is the defect class this replaced the removed <c>SequenceAllocator.Width</c> to catch (#69),
    /// and it is strictly
    /// stronger than comparing a total against an independently computed width: a compensating pair of
    /// errors, and an overlap between an <c>If</c>'s two branch ranges, both survive a total and fail here.
    /// </para>
    /// <para>
    /// It is a property of the emitted <em>text</em>, so a frame emitted inside a generated <c>if</c>
    /// counts once even though only one branch runs. What it does not catch is a frame that is never
    /// emitted at all: that shortens the run and stays dense. The <c>Snapshots</c> baselines are what pin
    /// frame presence and order.
    /// </para>
    /// <para>
    /// If a future node kind genuinely needs a hole in the numbering, this assertion failing is the
    /// intended prompt to revise the invariant deliberately, not to special-case the test.
    /// </para>
    /// </remarks>
    public static void AssertDense(string generatedCode)
    {
        var actual = InTextOrder(generatedCode);
        Assert.Equal(Enumerable.Range(0, actual.Count), actual);
    }
}

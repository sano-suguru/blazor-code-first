using System;
using System.Collections.Immutable;
using System.Linq;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Reads the <c>__builder</c> calls out of generated code, or out of a figure claiming to be some.
/// </summary>
/// <remarks>
/// One extractor for every test that compares emitted frames as text, for the reason
/// <see cref="SequenceArguments"/> records for sequence numbers (#69): two per-file scans of the same
/// output drift apart in whichever construct the file that owns them happens not to exercise, and the
/// drift is invisible from either side.
/// </remarks>
internal static class BuilderCalls
{
    /// <summary>
    /// The <c>__builder</c> calls in <paramref name="text"/>, in order and with their indentation
    /// dropped. Everything else is skipped, a figure's commentary included: what these callers compare
    /// is the frames, and a figure is allowed to say why it shows them.
    /// </summary>
    public static ImmutableArray<string> InTextOrder(string text) =>
    [
        .. text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("__builder.", StringComparison.Ordinal)),
    ];
}

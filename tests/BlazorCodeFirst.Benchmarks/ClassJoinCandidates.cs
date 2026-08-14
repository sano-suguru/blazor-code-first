using System.Runtime.CompilerServices;

namespace BlazorCodeFirst.Benchmarks;

/// <summary>
/// The two places the class channel's join can live, spelled side by side so #239 can be measured
/// rather than argued. <see cref="Generated"/> is what the compiler writes into each generated class
/// today; <see cref="Runtime"/> is the single span-taking method the runtime assembly would carry
/// instead.
/// </summary>
/// <remarks>
/// Both are transcriptions, not the shipping code. The generated arms are
/// <c>ClassChannel.JoinHelperCode(2)</c>, <c>(3)</c> and <c>(4)</c> with the helper renamed, and
/// <c>ClassChannelJoinTests.JoinHelperCode_WritesTheBodiesTheBenchmarkTranscribes</c> is what fails
/// when the generation rule moves out from under them. The runtime arm is the method #239 proposes,
/// which is not implemented anywhere. Measuring them here rather than through the generator is what
/// keeps the runtime's public surface out of an open decision: the emitter would have to be flipped to
/// call a member that only one of the two answers justifies shipping.
/// <para>
/// What that transcription costs is fidelity of the call, not of the work. Everything around the join
/// — <c>OpenElement</c>, the <c>AddAttribute</c> that receives the value, the content, the close — is
/// identical between the two candidates by construction, since only the expression passed to
/// <c>AddAttribute</c> differs. Measuring the expression alone therefore measures the whole of the
/// difference, and does it without the surrounding frame calls' variance on top.
/// </para>
/// <para>
/// #239 fixes the runtime candidate's signature and not its body, so the body here is the fastest one
/// that keeps the channel's rule: drop the null terms, then join what is left. Interleaving the
/// separators into the buffer instead, and concatenating that, was measured slower at every arity —
/// it writes and re-walks <c>2n-1</c> slots for <c>n</c> terms — and a candidate spelled that way
/// would have recorded the generated join's win rather than measured it.
/// </para>
/// </remarks>
internal static class ClassJoinCandidates
{
    /// <summary>
    /// The buffer the runtime join compacts the surviving terms into. Four elements covers every arity
    /// the class channel has been measured at; wider calls fall to the heap, where an element carrying
    /// five or more classes has already paid more than this.
    /// </summary>
    [InlineArray(4)]
    private struct TermBuffer
    {
        private string? _element0;
    }

    /// <summary>Two terms, as the generated class joins them.</summary>
    internal static string? Generated(string? a0, string? a1) =>
        a0 is null ? a1 : a1 is null ? a0 : string.Concat(a0, " ", a1);

    /// <summary>Three terms, as the generated class joins them.</summary>
    internal static string? Generated(string? a0, string? a1, string? a2) =>
        a0 is null ? Generated(a1, a2)
        : a1 is null ? Generated(a0, a2)
        : a2 is null ? Generated(a0, a1)
        : string.Concat(a0, " ", a1, " ", a2);

    /// <summary>Four terms, as the generated class joins them.</summary>
    internal static string? Generated(string? a0, string? a1, string? a2, string? a3) =>
        a0 is null ? Generated(a1, a2, a3)
        : a1 is null ? Generated(a0, a2, a3)
        : a2 is null ? Generated(a0, a1, a3)
        : a3 is null ? Generated(a0, a1, a2)
        : string.Concat(a0, " ", a1, " ", a2, " ", a3);

    /// <summary>
    /// Any number of terms, as one runtime method would join them. Null terms are dropped and earn no
    /// separator, which is the rule the generated ladder implements by deferring one arity at a time
    /// (#236).
    /// </summary>
    internal static string? Runtime(params ReadOnlySpan<string?> terms)
    {
        var buffer = new TermBuffer();
        Span<string?> surviving = terms.Length <= 4 ? buffer : new string?[terms.Length];

        var written = 0;
        foreach (var term in terms)
        {
            if (term is not null)
            {
                surviving[written++] = term;
            }
        }

        // string.Join places one separator between the terms it is given and hands back a lone term as
        // it stands, so dropping the nulls first is the whole of the rule.
        return written == 0 ? null : string.Join(' ', surviving[..written]);
    }
}

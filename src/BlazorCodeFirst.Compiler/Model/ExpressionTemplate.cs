using System;
using System.Collections.Immutable;
using System.Text;

namespace BlazorCodeFirst.Compiler;

internal abstract record ExpressionSegment;

internal sealed record LiteralExpressionSegment(string Text) : ExpressionSegment;

internal sealed record ParameterHoleExpressionSegment(int ParameterOrdinal) : ExpressionSegment;

/// <summary>
/// The compile-time constant value of an expression, when it has one. A <see langword="null"/> reference
/// (rather than any case below) means the expression is not a compile-time constant at all: it cannot be
/// serialized, and a view part local bound to it cannot be dropped, because its initializer may have
/// side effects.
/// </summary>
/// <remarks>
/// The cases are separate types, and not one nullable string, because the fold has to tell "renders
/// nothing" apart from "renders something the compiler cannot spell". A single <c>string? Text</c>
/// carried both as <c>null</c>, and <see cref="Generation.StaticMarkupSerializer"/> read that as "the
/// attribute is omitted" — right for a constant <see langword="null"/>, and wrong for every non-string
/// constant, which renders. Nothing was broken while <c>.Attr</c> was string-only; the
/// <see langword="bool"/> overload (#158) is what made a non-string attribute value reachable, and the
/// same route would have carried an <c>int</c> silently to markup with the attribute missing.
/// </remarks>
internal abstract record ConstantInfo;

/// <summary>A constant string, usable in markup as it stands.</summary>
internal sealed record StringConstant(string Text) : ConstantInfo;

/// <summary>
/// A constant <see langword="null"/>, of a string or of any other type. <c>AddAttribute</c> appends no
/// frame for it, so the fold writes nothing, and the two paths reach the same DOM.
/// </summary>
internal sealed record NullConstant : ConstantInfo;

/// <summary>
/// A constant <see langword="bool"/>: the one non-string value the markup path can express exactly.
/// Measured in Chromium (#158), <c>AddAttribute</c> renders <see langword="true"/> as <c>name=""</c> and
/// omits the attribute entirely for <see langword="false"/>, and markup can write both. A
/// <see langword="bool"/> has nothing to format, which is what separates it from
/// <see cref="RuntimeFormattedConstant"/>.
/// </summary>
internal sealed record BooleanConstant(bool Value) : ConstantInfo;

/// <summary>
/// A constant of any other type — an <c>int</c>, a <c>double</c>, a <c>DateTime</c>, an enum member.
/// Side-effect free, so a view part local bound to one may still be dropped, but never serializable:
/// measured (#158), <c>AddAttribute</c> formats such a value under whatever culture the formatting
/// thread carries at render time rather than under the culture in effect while the component builds its
/// frames, so the compiler cannot know the text it becomes (<c>3.5</c> reaches the DOM as <c>"3.5"</c>
/// under <c>en-US</c> and <c>"3,5"</c> under <c>de-DE</c>).
/// </summary>
internal sealed record RuntimeFormattedConstant : ConstantInfo;

/// <summary>
/// The caller-side content bound to one content hole (<c>Html.Slot</c> or a <c>View</c> parameter), captured
/// with everything needed to expand it later (#34).
/// </summary>
/// <remarks>
/// <para>
/// Substitution is lazy: the template is expanded where the callee names the hole, not at the call site.
/// That is what makes zero, one, and many references each work — every reference expands the subtree again
/// and consumes fresh preorder ordinals, so the generated local and loop-variable names cannot collide.
/// </para>
/// <para>
/// All three fields come from the <em>caller</em>, because the argument is an expression written there.
/// <see cref="ActiveMethodStack"/> is the load-bearing one: expanding under the callee's stack instead
/// reports <c>A(A(P["z"]))</c> as a recursion cycle, which it is not.
/// </para>
/// <para>
/// Transient expansion state, never part of the cached incremental model, so the
/// <see cref="ImmutableArray{T}"/> fields need no value-equal wrapper.
/// </para>
/// </remarks>
internal sealed record ContentArgument(
    RenderTemplateNode Template,
    ImmutableArray<SubstitutedArgument> Substitution,
    ImmutableArray<string> ActiveMethodStack);

/// <summary>
/// One value substituted for a parameter hole: the code text that replaces the hole, and that value's
/// compile-time constant when it has one. The two travel together so they cannot fall out of step in
/// length or order, which two parallel arrays would allow.
/// </summary>
/// <param name="Code">
/// The expression text. Empty for a content entry, whose hole is a node position and not a value; nothing
/// reads it there, and <see cref="ExpressionTemplate.Substitute"/> throws rather than emitting it.
/// </param>
internal readonly record struct SubstitutedArgument(string Code, ConstantInfo? Constant)
{
    /// <summary>
    /// The caller's content when this ordinal is a content hole, or <see langword="null"/> when it is an
    /// ordinary value. One array holds both kinds because holes of both kinds are indices into it; two
    /// parallel arrays could fall out of step, which is the same reason <see cref="Constant"/> travels
    /// beside <see cref="Code"/>.
    /// </summary>
    public ContentArgument? Content { get; init; }

    /// <summary>The content entry for a node hole, whose <see cref="Code"/> is never emitted.</summary>
    public static SubstitutedArgument ForContent(ContentArgument content) =>
        new(string.Empty, Constant: null) { Content = content };
}

internal sealed record ExpressionTemplate
{
    private readonly bool _containsHole;

    /// <remarks>
    /// Both derived facts are settled here because the record is immutable, so neither can change after
    /// construction and neither is worth recomputing per read. Coalescing adjacent literals is what makes
    /// <see cref="Segments"/> a canonical form — the same code text always the same segment array — which
    /// matters because <see cref="Segments"/> participates in the value equality the incremental generator
    /// caches on. <see cref="Analysis.ExpressionTemplateFactory"/> builds segments one gap and one
    /// replacement at a time and would otherwise hand out several spellings of one expression, differing
    /// only in where the source happened to be split.
    /// </remarks>
    private ExpressionTemplate(ImmutableArray<ExpressionSegment> segments, ConstantInfo? constant)
    {
        var canonical = CoalesceLiterals(segments);
        Segments = canonical;
        _containsHole = ContainsHole(canonical);
        Constant = constant;
    }

    public EquatableArray<ExpressionSegment> Segments { get; }

    /// <summary>
    /// The expression's compile-time constant value, or <see langword="null"/> when it has none. Set by
    /// <see cref="Analysis.ExpressionTemplateFactory.Create"/> from the semantic model; the fold in
    /// <see cref="Generation.StaticMarkupSerializer"/> reads it. Carried here rather than on the node
    /// records so that text content, attribute values, and the class channel all get it from one place.
    /// </summary>
    public ConstantInfo? Constant { get; }

    public static ExpressionTemplate Literal(string code) =>
        new([new LiteralExpressionSegment(code)], constant: null);

    /// <summary>
    /// A template for the constant string <paramref name="text"/>: its code is the C# literal, and its
    /// constant is that same text.
    /// </summary>
    /// <remarks>
    /// Here rather than at each call site because the two have to agree — the emitter reads the code, the
    /// fold in <see cref="Generation.StaticMarkupSerializer"/> reads the constant — and a site that spells
    /// the literal for itself is a site free to escape it differently. Asked by hole substitution below and
    /// by <see cref="ClassChannel"/>, which rebuilds a run of adjacent constant terms as one.
    /// </remarks>
    public static ExpressionTemplate StringLiteral(string text) =>
        new(
            [new LiteralExpressionSegment(
                Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(text, quote: true))],
            new StringConstant(text));

    public static ExpressionTemplate Create(
        ImmutableArray<ExpressionSegment> segments,
        ConstantInfo? constant = null) =>
        new(segments, constant);

    /// <summary>
    /// Replaces every parameter hole with its substituted code. When the template is exactly one hole and
    /// that argument is a compile-time constant <em>string</em>, the result carries the constant and its
    /// code becomes the constant literal instead of the local's name: the value is identical, and it is
    /// what lets a view part pass-through (<c>Span[title]</c>) fold. Only a string constant qualifies,
    /// because only a string can be re-spelled as a literal in the hole's place without changing the
    /// substituted code's type. A hole with surrounding text is left alone, because recomputing the value
    /// would need expression evaluation.
    /// </summary>
    /// <remarks>
    /// A hole-free template is returned as it stands. Substitution cannot change one — every segment would
    /// be copied to an equal value, and the segments are already canonical — and the expander begins every
    /// component with an empty substitution, so without this a component that calls no view part still
    /// rebuilt a template for each of its attribute values, class channels, handlers, text, keys, and
    /// conditions. The test costs nothing: the answer was settled at construction.
    /// </remarks>
    public ExpressionTemplate Substitute(ImmutableArray<SubstitutedArgument> arguments)
    {
        if (!_containsHole)
            return this;

        var segments = Segments.AsImmutableArray();
        if (segments.Length == 1
            && segments[0] is ParameterHoleExpressionSegment loneHole
            && ArgumentAt(loneHole, arguments) is { Constant: StringConstant { Text: var constantText } })
        {
            return StringLiteral(constantText);
        }

        var builder = ImmutableArray.CreateBuilder<ExpressionSegment>(segments.Length);
        foreach (var segment in segments)
        {
            builder.Add(segment switch
            {
                LiteralExpressionSegment literal => literal,
                ParameterHoleExpressionSegment hole =>
                    new LiteralExpressionSegment(ArgumentAt(hole, arguments).Code),
                _ => throw new InvalidOperationException(
                    $"Unknown expression segment '{segment.GetType().Name}'."),
            });
        }

        // The constant passes through unchanged. A template that has one never contains a parameter hole:
        // a hole is created only for an identifier bound to a view part parameter, and a parameter
        // reference is not a compile-time constant. So there is nothing here that substitution could
        // invalidate.
        return new ExpressionTemplate(builder.MoveToImmutable(), Constant);
    }

    public string ToCode()
    {
        var builder = new StringBuilder();
        foreach (var segment in Segments)
        {
            if (segment is not LiteralExpressionSegment literal)
            {
                throw new InvalidOperationException(
                    "Expression template still contains unbound parameter holes.");
            }

            builder.Append(literal.Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The code for this value where <c>AddAttribute</c> takes it, which is not always
    /// <see cref="ToCode"/>. That parameter is overloaded — <see langword="string"/>,
    /// <c>MulticastDelegate</c>, <see langword="object"/>, <see langword="bool"/> — so a bare
    /// <see langword="null"/> binds to none of them: measured (#234), the emitted
    /// <c>AddAttribute(1, "title", null)</c> is CS0121 between the <see langword="string"/> and
    /// <c>MulticastDelegate</c> overloads. A constant <see langword="null"/> therefore carries a cast,
    /// which is the whole difference; every other value has a type of its own and is written unchanged.
    /// </summary>
    /// <remarks>
    /// Asked by both paths that write an attribute value — the attribute channel and the class channel's
    /// lone-term case — so neither can be the one that forgets. The channel's two-or-more case does not
    /// ask: it writes a call to a join declared to return <see cref="string"/>, whatever its arguments are.
    /// <para>
    /// The alternative was to emit no frame for a constant null, which would match the fold exactly.
    /// Rejected because sequence numbers are allocated to <em>emitted</em> <c>RenderTreeBuilder</c> calls
    /// (<c>ARCHITECTURE.md</c> §2.7's <c>FrameWidth</c>): suppressing the emission would split this case
    /// from a constant <see langword="false"/> <see langword="bool"/>, which is emitted and appends no
    /// frame at runtime.
    /// </para>
    /// </remarks>
    public string ToAttributeValueCode() =>
        Constant is NullConstant ? $"(global::System.String?)({ToCode()})" : ToCode();

    private static bool ContainsHole(ImmutableArray<ExpressionSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (segment is ParameterHoleExpressionSegment)
                return true;
        }

        return false;
    }

    private static SubstitutedArgument ArgumentAt(
        ParameterHoleExpressionSegment hole,
        ImmutableArray<SubstitutedArgument> arguments)
    {
        System.Diagnostics.Debug.Assert(
            hole.ParameterOrdinal < arguments.Length,
            $"Hole ordinal {hole.ParameterOrdinal} exceeds substitution length {arguments.Length}; the scoped render-variable/view part ordinal invariant is broken.");

        var argument = arguments[hole.ParameterOrdinal];

        // A content ordinal reached through an *expression* hole means the analyzer classified a View-typed
        // reference as a value, which it must not: a content hole becomes a ContentHoleTemplateNode and is
        // substituted by ViewPartExpander, never here. Throwing keeps the empty Code from being emitted as
        // silently valid-looking generated source.
        if (argument.Content is not null)
        {
            throw new InvalidOperationException(
                $"Parameter ordinal {hole.ParameterOrdinal} is bound to caller content, which has no "
                    + "expression text. A content hole must be a ContentHoleTemplateNode.");
        }

        return argument;
    }

    /// <summary>
    /// Merges runs of adjacent literals so one expression has one segment array. Returns
    /// <paramref name="segments"/> unchanged when there is nothing to merge, which is the common case and
    /// the reason this can run on every construction: a template built as a single literal, or as strictly
    /// alternating literals and holes, is already canonical and allocates nothing here.
    /// </summary>
    private static ImmutableArray<ExpressionSegment> CoalesceLiterals(
        ImmutableArray<ExpressionSegment> segments)
    {
        if (!HasAdjacentLiterals(segments))
            return segments;

        var result = ImmutableArray.CreateBuilder<ExpressionSegment>();
        var text = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment is LiteralExpressionSegment literal)
            {
                text.Append(literal.Text);
                continue;
            }

            if (text.Length > 0)
            {
                result.Add(new LiteralExpressionSegment(text.ToString()));
                text.Clear();
            }

            result.Add(segment);
        }

        if (text.Length > 0)
            result.Add(new LiteralExpressionSegment(text.ToString()));

        return result.ToImmutable();
    }

    private static bool HasAdjacentLiterals(ImmutableArray<ExpressionSegment> segments)
    {
        for (var index = 1; index < segments.Length; index++)
        {
            if (segments[index] is LiteralExpressionSegment
                && segments[index - 1] is LiteralExpressionSegment)
            {
                return true;
            }
        }

        return false;
    }
}

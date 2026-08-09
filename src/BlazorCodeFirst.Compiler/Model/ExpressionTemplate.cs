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
/// serialized, and a composable local bound to it cannot be dropped, because its initializer may have
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
/// Side-effect free, so a composable local bound to one may still be dropped, but never serializable:
/// measured (#158), <c>AddAttribute</c> formats such a value under whatever culture the formatting
/// thread carries at render time rather than under the culture in effect while the component builds its
/// frames, so the compiler cannot know the text it becomes (<c>3.5</c> reaches the DOM as <c>"3.5"</c>
/// under <c>en-US</c> and <c>"3,5"</c> under <c>de-DE</c>).
/// </summary>
internal sealed record RuntimeFormattedConstant : ConstantInfo;

/// <summary>
/// One value substituted for a parameter hole: the code text that replaces the hole, and that value's
/// compile-time constant when it has one. The two travel together so they cannot fall out of step in
/// length or order, which two parallel arrays would allow.
/// </summary>
internal readonly record struct SubstitutedArgument(string Code, ConstantInfo? Constant);

internal sealed record ExpressionTemplate
{
    private ExpressionTemplate(ImmutableArray<ExpressionSegment> segments, ConstantInfo? constant)
    {
        Segments = segments;
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

    public static ExpressionTemplate Create(
        ImmutableArray<ExpressionSegment> segments,
        ConstantInfo? constant = null) =>
        new(segments, constant);

    /// <summary>
    /// Replaces every parameter hole with its substituted code. When the template is exactly one hole and
    /// that argument is a compile-time constant <em>string</em>, the result carries the constant and its
    /// code becomes the constant literal instead of the local's name: the value is identical, and it is
    /// what lets a composable pass-through (<c>Span[title]</c>) fold. Only a string constant qualifies,
    /// because only a string can be re-spelled as a literal in the hole's place without changing the
    /// substituted code's type. A hole with surrounding text is left alone, because recomputing the value
    /// would need expression evaluation.
    /// </summary>
    /// <remarks>
    /// A hole-free template is returned as it stands. Substitution cannot change one — every segment would
    /// be copied to an equal value — and the expander begins every component with an empty substitution, so
    /// without this a component that calls no composable still rebuilt a template for each of its attribute
    /// values, class channels, handlers, text, keys, and conditions. The scan it costs is one pass over the
    /// segments, which the loop below performs anyway.
    /// </remarks>
    public ExpressionTemplate Substitute(ImmutableArray<SubstitutedArgument> arguments)
    {
        var segments = Segments.AsImmutableArray();
        if (!ContainsHole(segments))
            return this;

        if (segments.Length == 1
            && segments[0] is ParameterHoleExpressionSegment loneHole
            && ArgumentAt(loneHole, arguments) is { Constant: StringConstant { Text: var constantText } constant })
        {
            return new ExpressionTemplate(
                [new LiteralExpressionSegment(
                    Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(constantText, quote: true))],
                constant);
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
        // a hole is created only for an identifier bound to a composable parameter, and a parameter
        // reference is not a compile-time constant. So there is nothing here that substitution could
        // invalidate.
        return new ExpressionTemplate(CoalesceLiterals(builder.MoveToImmutable()), Constant);
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
            $"Hole ordinal {hole.ParameterOrdinal} exceeds substitution length {arguments.Length}; the scoped render-variable/composable ordinal invariant is broken.");
        return arguments[hole.ParameterOrdinal];
    }

    private static ImmutableArray<ExpressionSegment> CoalesceLiterals(
        ImmutableArray<ExpressionSegment> segments)
    {
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
}

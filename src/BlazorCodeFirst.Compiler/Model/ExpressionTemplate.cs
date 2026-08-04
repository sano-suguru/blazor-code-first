using System;
using System.Collections.Immutable;
using System.Text;

namespace BlazorCodeFirst.Compiler;

internal abstract record ExpressionSegment;

internal sealed record LiteralExpressionSegment(string Text) : ExpressionSegment;

internal sealed record ParameterHoleExpressionSegment(int ParameterOrdinal) : ExpressionSegment;

/// <summary>
/// The compile-time constant value of an expression, when it has one. Distinguishes three states that the
/// fold predicate needs to tell apart, so it is a nullable struct rather than a bare string:
/// <c>null</c> means the expression is not a compile-time constant (so it cannot be serialized, and a
/// composable local bound to it cannot be dropped because the initializer may have side effects);
/// <c>{ Text: null }</c> means it is a constant with no usable string value, either a constant
/// <see langword="null"/> string (which <c>AddAttribute</c> omits entirely) or a constant of a
/// non-string type (side-effect free, but not directly serializable);
/// <c>{ Text: not null }</c> means it is a constant string usable in markup.
/// </summary>
internal readonly record struct ConstantInfo(string? Text);

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
    /// that argument is a compile-time constant, the result carries the constant and its code becomes the
    /// constant literal instead of the local's name: the value is identical, and it is what lets a
    /// composable pass-through (<c>Span[title]</c>) fold. A hole with surrounding text is left alone,
    /// because recomputing the value would need expression evaluation.
    /// </summary>
    public ExpressionTemplate Substitute(ImmutableArray<SubstitutedArgument> arguments)
    {
        var segments = Segments.AsImmutableArray();
        if (segments.Length == 1
            && segments[0] is ParameterHoleExpressionSegment loneHole
            && ArgumentAt(loneHole, arguments) is { Constant: { Text: { } constantText } constant })
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

    private static SubstitutedArgument ArgumentAt(
        ParameterHoleExpressionSegment hole,
        ImmutableArray<SubstitutedArgument> arguments)
    {
        System.Diagnostics.Debug.Assert(
            hole.ParameterOrdinal < arguments.Length,
            $"Hole ordinal {hole.ParameterOrdinal} exceeds substitution length {arguments.Length}; the ForEach/composable ordinal invariant is broken.");
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

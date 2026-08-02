using System;
using System.Collections.Immutable;
using System.Text;

namespace BlazorCodeFirst.Compiler;

internal abstract record ExpressionSegment;

internal sealed record LiteralExpressionSegment(string Text) : ExpressionSegment;

internal sealed record ParameterHoleExpressionSegment(int ParameterOrdinal) : ExpressionSegment;

internal sealed record ExpressionTemplate
{
    private ExpressionTemplate(ImmutableArray<ExpressionSegment> segments)
        => Segments = segments;

    public EquatableArray<ExpressionSegment> Segments { get; }

    public static ExpressionTemplate Literal(string code) =>
        new([new LiteralExpressionSegment(code)]);

    public static ExpressionTemplate Create(ImmutableArray<ExpressionSegment> segments) =>
        new(segments);

    public ExpressionTemplate Substitute(ImmutableArray<string> localNames)
    {
        var builder = ImmutableArray.CreateBuilder<ExpressionSegment>(Segments.Length);
        foreach (var segment in Segments)
        {
            builder.Add(segment switch
            {
                LiteralExpressionSegment literal => literal,
                ParameterHoleExpressionSegment hole =>
                    SubstituteHole(hole, localNames),
                _ => throw new InvalidOperationException(
                    $"Unknown expression segment '{segment.GetType().Name}'."),
            });
        }

        return new ExpressionTemplate(CoalesceLiterals(builder.MoveToImmutable()));
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

    private static LiteralExpressionSegment SubstituteHole(
        ParameterHoleExpressionSegment hole,
        ImmutableArray<string> localNames)
    {
        System.Diagnostics.Debug.Assert(
            hole.ParameterOrdinal < localNames.Length,
            $"Hole ordinal {hole.ParameterOrdinal} exceeds substitution length {localNames.Length}; the ForEach/composable ordinal invariant is broken.");
        return new LiteralExpressionSegment(localNames[hole.ParameterOrdinal]);
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

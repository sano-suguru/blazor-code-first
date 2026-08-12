using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// One element of a bracket child list: the expression the author wrote for it, and whether it arrived as
/// a collection-expression spread (<c>Div[[.. proj]]</c>) rather than as a child of its own.
/// </summary>
/// <param name="Expression">
/// A child's own expression, or a spread's operand — which is the sequence, not one of its items.
/// </param>
/// <param name="IsSpread">
/// Distinguishes the two, because they mean different things in the same position and only the reader
/// knows which it can take. A spread is one expression standing for zero or more children, so a reader
/// that treats it as a child emits the sequence where an item belongs.
/// </param>
internal readonly record struct ChildExpression(ExpressionSyntax Expression, bool IsSpread);

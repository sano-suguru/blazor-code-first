using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// One element of a bracket child list: the expression the author wrote for it, and whether it arrived as
/// a collection-expression spread (<c>Div[[.. proj]]</c>) rather than as a child of its own.
/// </summary>
/// <param name="Expression">
/// A child's own expression, or a spread's operand — which is the sequence, not one of its items. Every
/// producer stores the written expression of the element node itself, so this is always
/// <c>ExpressionElementSyntax.Expression</c> or <c>SpreadElementSyntax.Expression</c> and never anything
/// found below one. <see cref="IsSpread"/> says which, and a producer that stored something else while
/// answering the other would compile and silently mis-route a child.
/// </param>
/// <param name="IsSpread">
/// Distinguishes the two, because they mean different things in the same position and only the reader
/// knows which it can take. A spread is one expression standing for zero or more children, so a reader
/// that treats it as a child emits the sequence where an item belongs.
/// <para>
/// Carried rather than derived from <c>Expression.Parent</c>, although the invariant above makes the two
/// agree. The producers are the two binders, which already disagree about what they can recover, and the
/// consumers are two walks that must not each re-derive the rule; a stored answer is one place for it.
/// A reader holding an arbitrary node rather than a child list has no <see cref="ChildExpression"/> to
/// ask and tests the parent instead — <c>UnresolvedValueTypeScanner.IsSplicedSelect</c> is the one such
/// reader, and it is asking a different question from a different position.
/// </para>
/// </param>
internal readonly record struct ChildExpression(ExpressionSyntax Expression, bool IsSpread);

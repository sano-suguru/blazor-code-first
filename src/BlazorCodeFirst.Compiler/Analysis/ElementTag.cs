using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// The tag an <c>Element(tag)</c> argument carries, when BCF3009 accepts it.
/// </summary>
/// <remarks>
/// Two callers ask this and they have to agree: <c>RenderExpressionAnalyzer</c> reports the diagnostic
/// and builds the node, while <c>UnresolvedValueTypeScanner</c> gates the scan of the element's
/// children on the same answer, because a rejected element never reaches generated code and a report
/// about its children would be noise. The two held separate copies of the question until #394 widened
/// one of them, at which point a misspelled tag drew both a BCF3009 and a child diagnostic. One copy,
/// so the next widening cannot reopen that gap.
/// </remarks>
internal static class ElementTag
{
    /// <summary>
    /// Resolves <paramref name="expression"/> to the tag it carries, or answers <see langword="false"/>
    /// for the shapes BCF3009 rejects: an expression that is not a constant string, and a constant that
    /// is not spelled like a tag name.
    /// </summary>
    public static bool TryResolve(
        ExpressionSyntax? expression, ViewPartBodyContext context, out string tag)
    {
        tag = string.Empty;

        if (expression is null
            || context.SemanticModel.GetConstantValue(expression, context.CancellationToken)
                is not { HasValue: true, Value: string value }
            || !KnownSymbols.IsValidTagName(value))
        {
            return false;
        }

        tag = value;
        return true;
    }
}

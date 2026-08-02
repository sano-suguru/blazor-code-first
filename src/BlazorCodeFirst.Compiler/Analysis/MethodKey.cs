using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Produces a stable, value-comparable string identity for an <see cref="IMethodSymbol"/> so that a
/// composable definition discovered in one incremental step can be matched to a call site in another
/// without retaining Roslyn symbols across the pipeline.
/// </summary>
internal static class MethodKey
{
    public static string Create(IMethodSymbol method)
    {
        var definition = method.OriginalDefinition;
        var documentationId = definition.GetDocumentationCommentId();
        if (!string.IsNullOrEmpty(documentationId))
            return documentationId!;

        var parameters = string.Join(
            ",",
            definition.Parameters.Select(static parameter =>
                $"{parameter.RefKind}:{parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"));

        return $"{definition.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}." +
            $"{definition.Name}`{definition.Arity}({parameters})";
    }
}

using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Produces a stable, value-comparable string identity for an <see cref="IMethodSymbol"/> so that a
/// view part definition discovered in one incremental step can be matched to a call site in another
/// without retaining Roslyn symbols across the pipeline.
/// </summary>
/// <remarks>
/// The identity is read off <see cref="ISymbol.OriginalDefinition"/>, so only a symbol that is not reduced
/// may be passed: a reduced extension method's original definition is still reduced, and its documentation
/// comment id does not name the <c>this</c> parameter that the declaration's does, so one method would key
/// two ways depending on how its call was spelled. Nothing here walks
/// <see cref="IMethodSymbol.ReducedFrom"/> to absorb that, because a view part is never an extension
/// member (<c>DESIGN.md</c> §4.3, #203).
/// </remarks>
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

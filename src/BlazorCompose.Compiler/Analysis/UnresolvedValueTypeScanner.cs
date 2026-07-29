using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Sweeps a design-time expression that failed to translate for unresolved type references in value
/// positions, reporting BC3015 where the normal <see cref="ExpressionTemplateFactory"/> path could not
/// run because an outer invocation had no method symbol.
/// </summary>
internal static class UnresolvedValueTypeScanner
{
    /// <summary>
    /// Records BC3015 for unresolved type references that would otherwise be emitted as dynamic values.
    /// Type arguments of <c>Html.Component&lt;T&gt;</c> are owned by BC3012 and are deliberately excluded.
    /// </summary>
    public static void Report(ExpressionSyntax root, ComposableBodyContext context)
    {
        foreach (var name in root.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (IsInsideNameof(name) || IsComponentTypeArgument(name, context))
                continue;

            _ = ExpressionTemplateFactory.TryReportUnresolvedType(name, context);
        }
    }

    private static bool IsComponentTypeArgument(SimpleNameSyntax name, ComposableBodyContext context)
    {
        foreach (var generic in name.AncestorsAndSelf().OfType<GenericNameSyntax>())
        {
            if (!generic.TypeArgumentList.Arguments.Any(argument => argument.Span.Contains(name.Span)))
                continue;

            var invocation = generic.Parent switch
            {
                InvocationExpressionSyntax direct when direct.Expression == generic => direct,
                MemberAccessExpressionSyntax access
                    when access.Parent is InvocationExpressionSyntax memberInvocation => memberInvocation,
                _ => null,
            };
            if (invocation is null)
                continue;

            var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
            if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method)
                continue;

            if (UnresolvedComponentTypeScanner.IsComponentFactory(
                    method,
                    context.KnownSymbols.HtmlComponent,
                    context.KnownSymbols.HtmlComponentWithChildren))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInsideNameof(SimpleNameSyntax name) =>
        name.Ancestors().OfType<InvocationExpressionSyntax>().Any(static invocation =>
            invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" });
}

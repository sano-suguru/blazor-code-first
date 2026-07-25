using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCompose.Compiler.Diagnostics;

/// <summary>
/// Reports BC3001 when a <c>Body</c> getter directly mutates instance state of the
/// containing component during rendering.
/// </summary>
/// <remarks>
/// <para>
/// The initial detectable boundary covers statically identifiable direct writes: field assignments,
/// property assignments, and increment/decrement operators whose target is an instance member of the
/// containing component.  The recognized deferred event handler lambda — the sole argument of a
/// Html-mirror <c>View.OnClick(...)</c> call — is excluded because state mutations there are the
/// correct location for imperative state transitions and execute after rendering, not during it.
/// </para>
/// <para>
/// Arbitrary interprocedural side effects (mutations inside a helper method called from Body) are
/// not guaranteed to be detected by this first-slice implementation.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RenderMutationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.BC3001];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(
            AnalyzeMutation,
            OperationKind.Increment,
            OperationKind.Decrement,
            OperationKind.SimpleAssignment,
            OperationKind.CompoundAssignment);
    }

    // ---------------------------------------------------------------------------
    // Core analysis
    // ---------------------------------------------------------------------------

    private static void AnalyzeMutation(OperationAnalysisContext ctx)
    {
        // Condition 1: The operation targets an instance field or property.
        var targetSymbol = GetInstanceMemberTarget(ctx.Operation);
        if (targetSymbol is null) return;

        // Condition 2: The operation is syntactically inside the Body getter of a
        // ComposeComponentBase subclass.
        var semanticModel = ctx.Operation.SemanticModel;
        if (semanticModel is null) return;

        if (!TryGetBodyOwnerType(ctx.Operation.Syntax, semanticModel, out var ownerType)) return;

        // The target must belong to the same component (not a field on a nested type, etc.).
        if (!SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingType, ownerType)) return;

        // Condition 3: The operation must not be inside a recognized deferred event handler lambda
        // (classified as DeferredEventHandler — mutations there execute after rendering): the
        // Html-mirror View.OnClick(...) argument lambda.
        if (IsInsideDeferredEventHandlerLambda(ctx.Operation.Syntax, semanticModel)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.BC3001,
            ctx.Operation.Syntax.GetLocation(),
            targetSymbol.Name));
    }

    // ---------------------------------------------------------------------------
    // Helpers — target extraction
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the field or property symbol targeted by a mutation operation when it is an
    /// instance member, or <see langword="null"/> otherwise.
    /// </summary>
    private static ISymbol? GetInstanceMemberTarget(IOperation operation)
    {
        IOperation? target = operation switch
        {
            IIncrementOrDecrementOperation op => op.Target,
            ISimpleAssignmentOperation op => op.Target,
            ICompoundAssignmentOperation op => op.Target,
            _ => null,
        };

        if (target is null) return null;

        return target switch
        {
            IFieldReferenceOperation { Field: { IsStatic: false } field } => field,
            IPropertyReferenceOperation { Property: { IsStatic: false } prop } => prop,
            _ => null,
        };
    }

    // ---------------------------------------------------------------------------
    // Helpers — Body getter detection
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Walks the syntax ancestors of <paramref name="operationSyntax"/> to find an
    /// <c>override Body</c> property declaration and verifies via the semantic model
    /// that it belongs to a <c>ComposeComponentBase</c> subclass.
    /// </summary>
    private static bool TryGetBodyOwnerType(
        SyntaxNode operationSyntax,
        SemanticModel semanticModel,
        out INamedTypeSymbol? ownerType)
    {
        ownerType = null;
        var node = operationSyntax.Parent;
        while (node is not null)
        {
            if (node is PropertyDeclarationSyntax propDecl &&
                propDecl.Identifier.Text == "Body" &&
                propDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
            {
                if (semanticModel.GetDeclaredSymbol(propDecl) is IPropertySymbol prop &&
                    prop.ContainingType is INamedTypeSymbol type &&
                    ComposeComponentBaseFacts.InheritsFromComposeComponentBase(type))
                {
                    ownerType = type;
                    return true;
                }
                return false;
            }
            node = node.Parent;
        }
        return false;
    }

    // ---------------------------------------------------------------------------
    // Helpers — OnClick handler context detection
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="operationSyntax"/> is enclosed in a
    /// lambda that is syntactically a recognized deferred event handler argument: the single
    /// argument of a Html-mirror <c>View.OnClick(...)</c> call. Stops at the first enclosing lambda
    /// and returns <see langword="false"/> when that lambda does not match; this ensures that
    /// If-content lambdas (which remain rendering contexts) are still reported.
    /// </summary>
    private static bool IsInsideDeferredEventHandlerLambda(
        SyntaxNode operationSyntax,
        SemanticModel semanticModel)
    {
        var node = operationSyntax.Parent;
        while (node is not null)
        {
            if (node is LambdaExpressionSyntax lambda)
            {
                return IsOnClickHandlerArgument(lambda, semanticModel);
                // The nearest enclosing lambda is not a recognized handler; stop here.
                // (If-content lambdas and other lambdas remain rendering contexts.)
            }
            node = node.Parent;
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="lambda"/> is the (sole, reduced) argument
    /// of a Html-mirror <c>Decorations.OnClick(this View, Action)</c> invocation.
    /// </summary>
    private static bool IsOnClickHandlerArgument(LambdaExpressionSyntax lambda, SemanticModel semanticModel)
    {
        // Assumes the fluent (reduced) call form, where the handler is Arguments[0]. A non-fluent
        // static call, Decorations.OnClick(view, handler), would place the handler at Arguments[1]
        // and is intentionally not matched here — it fails safe by still reporting BC3001 rather than
        // silently hiding a real mutation.
        if (lambda.Parent is ArgumentSyntax arg &&
            arg.Parent is ArgumentListSyntax argList &&
            argList.Parent is InvocationExpressionSyntax invocation &&
            argList.Arguments.Count >= 1 &&
            argList.Arguments[0] == arg)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            // Anchor the namespace check to the global root, consistent with
            // ComposeComponentBaseFacts.InheritsFromComposeComponentBase, so that a user-defined type
            // in e.g. Some.BlazorCompose.Decorations cannot spoof the exclusion.
            if (symbolInfo.Symbol is IMethodSymbol { Name: "OnClick", IsExtensionMethod: true } onClickMethod &&
                (onClickMethod.ReducedFrom ?? onClickMethod).ContainingType is { Name: "Decorations" } decorationsType &&
                decorationsType.ContainingNamespace is { IsGlobalNamespace: false, Name: "BlazorCompose" } ns &&
                ns.ContainingNamespace.IsGlobalNamespace)
            {
                return true;
            }
        }
        return false;
    }
}

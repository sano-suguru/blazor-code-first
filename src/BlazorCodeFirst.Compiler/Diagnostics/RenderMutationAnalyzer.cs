using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCodeFirst.Compiler.Diagnostics;

/// <summary>
/// Reports BCF3001 when a BlazorCodeFirst base's design-time expression getter (<c>Body</c> on
/// <c>BodyComponentBase</c>, <c>Chrome</c> on <c>ChromeLayoutBase</c>) directly mutates instance
/// state of the containing component during rendering.
/// </summary>
/// <remarks>
/// <para>
/// The initial detectable boundary covers statically identifiable direct writes: field assignments,
/// property assignments, and increment/decrement operators whose target is an instance member of the
/// containing component. The recognized deferred event handlers — the handler argument of a
/// Html-mirror <c>View.OnClick(...)</c> or <c>View.On(...)</c> call, and the setter argument of a
/// two-way <c>.Bind(...)</c> call — are excluded because state mutations there are the correct
/// location for imperative state transitions and execute after rendering, not during it. The getter
/// argument of a <c>.Bind(...)</c> call is not exempt: it is evaluated while the frames are built, so
/// a mutation there is still a one-way-flow break. A mutation is exempt when <em>any</em> enclosing
/// lambda (not just the innermost) is such a handler argument, so nested lambdas inside a deferred
/// handler body remain exempt as well.
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
        [DiagnosticDescriptors.BCF3001];

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

        // Condition 2: The operation is syntactically inside the design-time expression getter
        // (Body or Chrome) of a BlazorCodeFirst base subclass.
        var semanticModel = ctx.Operation.SemanticModel;
        if (semanticModel is null) return;

        if (!TryGetDesignTimeExpressionOwnerType(
                ctx.Operation.Syntax, semanticModel, out var ownerType, out var expressionName))
        {
            return;
        }

        // The target must belong to the same component (not a field on a nested type, etc.).
        if (!SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingType, ownerType)) return;

        // Condition 3: The operation must not be inside a recognized deferred event handler lambda
        // (classified as DeferredEventHandler, mutations there execute after rendering): the
        // Html-mirror View.OnClick(...) argument lambda.
        if (IsInsideDeferredEventHandlerLambda(ctx.Operation.Syntax, semanticModel)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.BCF3001,
            ctx.Operation.Syntax.GetLocation(),
            targetSymbol.Name,
            expressionName));
    }

    // ---------------------------------------------------------------------------
    // Helpers, target extraction
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
    // Helpers, Body getter detection
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Walks the syntax ancestors of <paramref name="operationSyntax"/> to find an <c>override</c>
    /// property declaration and verifies via the semantic model that it is the design-time expression
    /// (<c>Body</c> or <c>Chrome</c>, resolved semantically) of a BlazorCodeFirst base subclass.
    /// </summary>
    private static bool TryGetDesignTimeExpressionOwnerType(
        SyntaxNode operationSyntax,
        SemanticModel semanticModel,
        out INamedTypeSymbol? ownerType,
        out string? expressionName)
    {
        ownerType = null;
        expressionName = null;
        var node = operationSyntax.Parent;
        while (node is not null)
        {
            if (node is PropertyDeclarationSyntax propDecl &&
                propDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)))
            {
                if (semanticModel.GetDeclaredSymbol(propDecl) is IPropertySymbol prop &&
                    prop.ContainingType is INamedTypeSymbol type &&
                    DesignTimeBaseFacts.FindDesignTimeExpressionName(type) is { } name &&
                    prop.Name == name)
                {
                    ownerType = type;
                    expressionName = name;
                    return true;
                }
                return false;
            }
            node = node.Parent;
        }
        return false;
    }

    // ---------------------------------------------------------------------------
    // Helpers, deferred event handler context detection
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="operationSyntax"/> is enclosed, at any
    /// nesting depth, by a lambda that is syntactically a recognized deferred event handler
    /// argument: the handler argument of a Html-mirror <c>View.OnClick(...)</c> or <c>View.On(...)</c>
    /// call, or the setter argument of a two-way <c>.Bind(...)</c> call. Every enclosing lambda is
    /// checked (not just the innermost), so a mutation inside a nested lambda that itself lives
    /// inside a recognized handler lambda (e.g. <c>OnClick(async () => items.ForEach(i => total +=
    /// i))</c>) is still exempt. If-content lambdas and other non-handler lambdas do not match and
    /// analysis continues outward.
    /// </summary>
    private static bool IsInsideDeferredEventHandlerLambda(
        SyntaxNode operationSyntax,
        SemanticModel semanticModel)
    {
        var node = operationSyntax.Parent;
        while (node is not null)
        {
            if (node is LambdaExpressionSyntax lambda &&
                (IsDeferredEventHandlerArgument(lambda, semanticModel) ||
                 IsBindSetterArgument(lambda, semanticModel)))
            {
                return true;
            }
            node = node.Parent;
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="lambda"/> is the handler argument of a
    /// Html-mirror <c>Decorations.OnClick(...)</c> or <c>Decorations.On(...)</c> invocation. The handler
    /// is identified by the parameter it binds to, not by its position: a named argument
    /// (<c>.On(handler: h, eventName: "onclick")</c>) can put it anywhere in the list.
    /// </summary>
    private static bool IsDeferredEventHandlerArgument(LambdaExpressionSyntax lambda, SemanticModel semanticModel)
    {
        if (lambda.Parent is not ArgumentSyntax arg ||
            arg.Parent is not ArgumentListSyntax argList ||
            argList.Parent is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        // Anchor the namespace to the global root so a user-defined Some.BlazorCodeFirst.Decorations
        // cannot spoof the exclusion.
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { IsExtensionMethod: true } method ||
            method.Name is not ("OnClick" or "On") ||
            (method.ReducedFrom ?? method).ContainingType is not { Name: "Decorations" } decorationsType ||
            decorationsType.ContainingNamespace is not { IsGlobalNamespace: false, Name: "BlazorCodeFirst" } ns ||
            !ns.ContainingNamespace.IsGlobalNamespace)
        {
            return false;
        }

        return BindsToDelegateParameter(arg, invocation, semanticModel);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="argument"/> binds to a delegate-typed
    /// parameter of <paramref name="invocation"/>, the <c>Action</c> or <c>Func&lt;Task&gt;</c> handler of
    /// <c>OnClick</c>/<c>On</c>, as opposed to the <c>string</c> event name.
    /// </summary>
    private static bool BindsToDelegateParameter(
        ArgumentSyntax argument, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation)
            return false;

        foreach (var operationArgument in operation.Arguments)
        {
            // Reference equality is safe here even though FactoryArguments.Bind's default arm cannot
            // rely on it (see the comment there): the elision that defeats a raw Syntax comparison only
            // strips a bare null-forgiving suppression from the operation tree, and `argument` here is
            // always a lambda literal, never a suppressed identifier, so operationArgument.Syntax
            // always points back at the same ArgumentSyntax the caller matched on.
            if (operationArgument.Syntax != argument)
                continue;

            return operationArgument.Parameter?.Type.TypeKind == TypeKind.Delegate;
        }

        return false;
    }

    // ---------------------------------------------------------------------------
    // Helpers, Bind setter argument detection
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="lambda"/> is the setter argument of a
    /// two-way <c>.Bind(...)</c> call: the element decoration <c>Decorations.Bind(...)</c> (an
    /// extension method on the element builder) or the component decoration
    /// <c>ComponentView&lt;TComponent&gt;.Bind(...)</c> (an instance method on the component
    /// builder). The setter runs after the render, in response to the bound event, so a mutation
    /// there is not a one-way-flow break. The getter argument of the same call is deliberately not
    /// recognized here: it is evaluated while the frames are built, so a mutation there must still
    /// be reported.
    /// </summary>
    private static bool IsBindSetterArgument(LambdaExpressionSyntax lambda, SemanticModel semanticModel)
    {
        if (lambda.Parent is not ArgumentSyntax arg ||
            arg.Parent is not ArgumentListSyntax argList ||
            argList.Parent is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            method.Name != "Bind" ||
            !IsRecognizedBindMethod(method))
        {
            return false;
        }

        return IsSetterParameter(method, GetBoundParameter(arg, invocation, semanticModel));
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="method"/> is one of the two known
    /// <c>Bind</c> declarations. Anchored the same way as the OnClick/On check above: the namespace
    /// is rooted at the global namespace, and the declaring type name plus extension-method-ness
    /// together rule out an unrelated same-named <c>Bind</c> method.
    /// </summary>
    private static bool IsRecognizedBindMethod(IMethodSymbol method)
    {
        var declaringType = (method.ReducedFrom ?? method).ContainingType;
        if (declaringType is not { ContainingNamespace: { IsGlobalNamespace: false, Name: "BlazorCodeFirst" } ns } ||
            !ns.ContainingNamespace.IsGlobalNamespace)
        {
            return false;
        }

        // The element decoration is an extension method declared on Decorations; the component
        // decoration is an instance method declared on ComponentView<TComponent>.
        return (method.IsExtensionMethod && declaringType.Name == "Decorations") ||
               (!method.IsExtensionMethod && declaringType.Name == "ComponentView");
    }

    /// <summary>
    /// Returns the parameter of the resolved method that <paramref name="argument"/> binds to, or
    /// <see langword="null"/> when the operation tree does not resolve it.
    /// </summary>
    private static IParameterSymbol? GetBoundParameter(
        ArgumentSyntax argument, InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation)
            return null;

        foreach (var operationArgument in operation.Arguments)
        {
            // See the reference-equality note on BindsToDelegateParameter above; the same reasoning
            // applies here, since argument is likewise always a lambda literal.
            if (operationArgument.Syntax != argument)
                continue;

            return operationArgument.Parameter;
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="candidate"/> is the setter parameter of
    /// <paramref name="bindMethod"/>: a delegate taking exactly the bound value
    /// (<c>Action&lt;T&gt;</c> or <c>Func&lt;T, Task&gt;</c>). Identified by finding the same
    /// overload's getter parameter — a zero-argument <c>Func&lt;T&gt;</c> — and comparing
    /// <paramref name="candidate"/>'s single delegate parameter against the getter's return type T,
    /// rather than by argument position: the element and component surfaces put the setter at a
    /// different ordinal, and the component surface additionally declares a selector parameter
    /// (<c>Func&lt;TComponent, TValue&gt;</c>) whose own single delegate parameter would otherwise be
    /// mistaken for a setter by arity alone.
    /// </summary>
    private static bool IsSetterParameter(IMethodSymbol bindMethod, IParameterSymbol? candidate)
    {
        if (candidate?.Type is not INamedTypeSymbol { DelegateInvokeMethod: { Parameters.Length: 1 } setterInvoke })
            return false;

        foreach (var parameter in bindMethod.Parameters)
        {
            if (parameter.Type is INamedTypeSymbol { DelegateInvokeMethod: { Parameters.Length: 0 } getterInvoke })
            {
                return SymbolEqualityComparer.Default.Equals(
                    setterInvoke.Parameters[0].Type, getterInvoke.ReturnType);
            }
        }

        return false;
    }
}

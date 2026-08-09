using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Analysis;
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
/// containing component. The recognized deferred event handlers — the handler argument of an event
/// decoration (<c>.OnClick(...)</c> or <c>.On(...)</c>), and the setter argument of a two-way
/// <c>.Bind(...)</c> call — are excluded because state mutations there are the correct
/// location for imperative state transitions and execute after rendering, not during it. The getter
/// argument of a <c>.Bind(...)</c> call is not exempt: it is evaluated while the frames are built, so
/// a mutation there is still a one-way-flow break. A mutation is exempt when <em>any</em> enclosing
/// lambda (not just the innermost) is such a handler argument, so nested lambdas inside a deferred
/// handler body remain exempt as well.
/// </para>
/// <para>
/// Which calls those are is asked of <see cref="Analysis.KnownSymbols.ClassifySurfaceMethod"/> — the one
/// place the compiler records what a surface method is — rather than decided from the method's name here.
/// The exemption set is therefore a consequence of what the referenced runtime declares and the generator
/// already recognizes: a decoration added there is exempt without an edit to this file, and a decoration
/// this compiler does not recognize cannot claim the exemption by sharing a name with one that is (#194).
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

        context.RegisterCompilationStartAction(static startContext =>
        {
            // The surface is resolved out of the referenced runtime once per compilation and every
            // recognition below is by symbol identity against it. Nothing here reads a method name: the
            // exemptions follow from what KnownSymbols classified, so a decoration added to the runtime
            // becomes exempt by being registered there rather than by being listed here as well.
            //
            // Nothing is analyzed at all when the surface does not resolve, which happens when
            // BlazorCodeFirst.Html is absent or ambiguous between two references. Both leave the whole
            // body reported as BCF1003 already (see KnownSymbols's constructor), and neither leaves any
            // way to tell a deferred handler from a render-time mutation — so a missing BCF3001 is the
            // right degradation and a spurious one on a correct handler would be the wrong one.
            if (KnownSymbols.TryCreate(startContext.Compilation) is not { } knownSymbols)
                return;

            startContext.RegisterOperationAction(
                operationContext => AnalyzeMutation(operationContext, knownSymbols),
                OperationKind.Increment,
                OperationKind.Decrement,
                OperationKind.SimpleAssignment,
                OperationKind.CompoundAssignment);
        });
    }

    // ---------------------------------------------------------------------------
    // Core analysis
    // ---------------------------------------------------------------------------

    private static void AnalyzeMutation(OperationAnalysisContext ctx, KnownSymbols knownSymbols)
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

        // Condition 3: The operation must not be inside a recognized deferred handler lambda, where
        // mutations execute after rendering rather than during it: an event decoration's handler argument
        // or a .Bind setter argument.
        if (IsInsideDeferredEventHandlerLambda(ctx.Operation.Syntax, semanticModel, knownSymbols)) return;

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
    /// nesting depth, by a lambda that is a recognized deferred handler argument. Every enclosing lambda
    /// is checked (not just the innermost), so a mutation inside a nested lambda that itself lives
    /// inside a recognized handler lambda (e.g. <c>OnClick(async () => items.ForEach(i => total +=
    /// i))</c>) is still exempt. If-content lambdas and other non-handler lambdas do not match and
    /// analysis continues outward.
    /// </summary>
    private static bool IsInsideDeferredEventHandlerLambda(
        SyntaxNode operationSyntax,
        SemanticModel semanticModel,
        KnownSymbols knownSymbols)
    {
        var node = operationSyntax.Parent;
        while (node is not null)
        {
            if (node is LambdaExpressionSyntax lambda &&
                IsDeferredHandlerArgument(lambda, semanticModel, knownSymbols))
            {
                return true;
            }
            node = node.Parent;
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="lambda"/> is a deferred handler argument: the
    /// handler of an event decoration (a named event shortcut such as <c>.OnClick(...)</c>, or
    /// <c>.On(...)</c>), or the setter of a two-way <c>.Bind(...)</c> — the element decoration
    /// <c>Decorations.Bind(...)</c> or the component decoration
    /// <c>ComponentView&lt;TComponent&gt;.Bind(...)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One predicate over the classification rather than one per decoration group: which lambda counts as
    /// the deferred one is the only thing that differs between them, and both groups reach it the same way
    /// — unwrap the lambda to its argument, resolve the invocation, ask
    /// <see cref="KnownSymbols.ClassifySurfaceMethod"/>. Two predicates meant resolving the same
    /// invocation twice whenever the first declined, and two copies of that prologue to keep in step.
    /// </para>
    /// <para>
    /// Each arm identifies its lambda by the parameter it binds to, never by argument position: a named
    /// argument (<c>.On(handler: h, eventName: "onclick")</c>) can put the handler anywhere in the list.
    /// The <c>.Bind</c> getter is deliberately not a deferred position — it is evaluated while the frames
    /// are built, so a mutation there must still be reported — and neither is the component spelling's
    /// selector, which names a parameter rather than carrying a value. <see cref="BindParameters.IsSetter"/>
    /// separates the setter from both, by position rather than by delegate shape (#206).
    /// </para>
    /// </remarks>
    private static bool IsDeferredHandlerArgument(
        LambdaExpressionSyntax lambda, SemanticModel semanticModel, KnownSymbols knownSymbols)
    {
        if (lambda.Parent is not ArgumentSyntax arg ||
            arg.Parent is not ArgumentListSyntax argList ||
            argList.Parent is not InvocationExpressionSyntax invocation ||
            semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return knownSymbols.ClassifySurfaceMethod(method) switch
        {
            // The handler, as opposed to .On's string event name.
            SurfaceMethodKind.EventShortcut or SurfaceMethodKind.On =>
                GetBoundParameter(arg, invocation, semanticModel)?.Type.TypeKind == TypeKind.Delegate,
            SurfaceMethodKind.Bind or SurfaceMethodKind.ComponentBind =>
                KnownSymbols.TryGetBindParameters(method, out var bind)
                    && bind.IsSetter(GetBoundParameter(arg, invocation, semanticModel)),
            _ => false,
        };
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
            // Reference equality is safe here even though FactoryArguments.Bind's default arm cannot
            // rely on it (see the comment there): the elision that defeats a raw Syntax comparison only
            // strips a bare null-forgiving suppression from the operation tree, and `argument` is
            // always a lambda literal on both paths into this method, never a suppressed identifier, so
            // operationArgument.Syntax always points back at the same ArgumentSyntax the caller matched
            // on.
            if (operationArgument.Syntax != argument)
                continue;

            return operationArgument.Parameter;
        }

        return null;
    }

}

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
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
/// decoration (<c>.OnClick(...)</c> or <c>.On(...)</c>), the setter argument of a two-way
/// <c>.Bind(...)</c> call, and the value of a component's <c>.Param(...)</c>, which the child invokes
/// when it raises the callback — are excluded because state mutations there are the correct
/// location for imperative state transitions and execute after rendering, not during it. The getter
/// argument of a <c>.Bind(...)</c> call is not exempt: it is evaluated while the frames are built, so
/// a mutation there is still a one-way-flow break. A mutation is exempt when <em>any</em> enclosing
/// anonymous function (not just the innermost) is such a handler argument, so nested lambdas inside a
/// deferred handler body remain exempt as well. Both spellings count: a lambda and the
/// <c>delegate(T v) { … }</c> anonymous method are the same argument to the same parameter.
/// </para>
/// <para>
/// Which calls those are is asked of <see cref="Analysis.KnownSymbols.ClassifySurfaceMethod"/> — the one
/// place the compiler records what a surface method is — rather than decided from the method's name here.
/// A decoration this compiler does not recognize therefore cannot claim the exemption by sharing a name
/// with one that is (#194). One deferred position is not a surface method at all: a handler written for a
/// component's <c>EventCallback</c> parameter sits inside <c>EventCallback.Factory.Create</c>, which is
/// Blazor's call. That one is asked of
/// <see cref="Analysis.KnownSymbols.IsEventCallbackFactoryMethod"/>, so the spelling is still resolved
/// against a symbol there rather than matched by name here (#385).
/// </para>
/// <para>
/// The classification decides <em>whether</em> a method may carry a deferred delegate; it does not decide
/// on its own <em>which</em> of that method's parameters is the deferred one, so a new channel carrying
/// one has to be listed in <see cref="IsDeferredHandlerArgument"/>'s switch. That is a real edit and not a
/// free consequence, which this paragraph used to claim it was: <c>.Ref</c> (#309) landed with the switch
/// unchanged and made the capture spelling its own documentation prescribes a BCF3001, because a capture
/// action assigns the captured value and that assignment is the whole point of the channel. The default
/// arm is <see langword="false"/>, so the failure is a spurious error rather than a silent exemption,
/// which is the right way round; <c>RenderMutationAnalyzerTests</c> now carries a case per channel.
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
        // Condition 1: The operation is inside the design-time expression getter (Body or Chrome) of a
        // BlazorCodeFirst base subclass. Asked first because it is both the cheapest of the three and by
        // far the most selective: it is a property of the member being analyzed rather than of the
        // operation, so it rejects every mutation in every ordinary method without looking at one. It was
        // the second condition while it was a syntax walk, when asking it first would have meant walking
        // for operations that condition 2 rules out (#220).
        if (!TryGetDesignTimeExpression(ctx.ContainingSymbol, out var expression))
            return;

        // Condition 2: The operation targets an instance field or property.
        var targetSymbol = GetInstanceMemberTarget(ctx.Operation);
        if (targetSymbol is null) return;

        // The target must belong to the same component (not a field on a nested type, etc.).
        if (!SymbolEqualityComparer.Default.Equals(targetSymbol.ContainingType, expression.ContainingType))
            return;

        // Condition 3: The operation must not be inside a recognized deferred handler, where mutations
        // execute after rendering rather than during it: an event decoration's handler argument or a
        // .Bind setter argument.
        if (IsInsideDeferredEventHandler(ctx.Operation, knownSymbols)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.BCF3001,
            ctx.Operation.Syntax.GetLocation(),
            targetSymbol.Name,
            expression.Name));
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
    /// Whether <paramref name="containingSymbol"/> is the getter of a BlazorCodeFirst base subclass's
    /// design-time expression override (<c>Body</c> or <c>Chrome</c>, resolved semantically), yielding the
    /// overridden property it belongs to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The symbol an operation is analyzed under, rather than a walk to its enclosing declaration. Roslyn
    /// hands the analyzer the accessor directly, for an expression-bodied getter, a block-bodied one, and a
    /// mutation nested inside a lambda inside either — where a lambda is a symbol of its own but never the
    /// one an operation action is registered against (#220). What a walk to the first enclosing
    /// <c>PropertyDeclarationSyntax</c> reconstructed from an <c>override</c> token and a
    /// <c>GetDeclaredSymbol</c> call, this is handed, and it needs no <see cref="SemanticModel"/> at all —
    /// which is what leaves this analyzer with no dependency on <c>Microsoft.CodeAnalysis.CSharp</c>.
    /// </para>
    /// <para>
    /// This is the condition every mutation operation in the compilation is asked, the other two being
    /// reached only once it has passed. So the cost that matters is the one paid by an ordinary
    /// <c>_x = y</c> in an ordinary method, which is now a type test rather than a climb to the compilation
    /// unit (the old walk had no upper bound: a non-<c>override</c> property did not stop it either). Being
    /// a property of the member under analysis rather than of the operation is also what makes it the most
    /// selective of the three, and therefore the one to ask first.
    /// </para>
    /// <para>
    /// <see cref="MethodKind.PropertyGet"/> narrows to the getter where the syntax walk found the property
    /// whichever accessor the mutation sat in. Nothing is lost by that: both bases declare their expression
    /// <c>{ get; }</c> only, so an override carrying a setter does not compile. An auto-property initializer
    /// is not a missing case either: Roslyn analyzes one under the property itself rather than under an
    /// accessor, so it fails the <see cref="IMethodSymbol"/> test above — and C# forbids <c>this</c> there
    /// in any case, so no instance-member mutation can appear in one.
    /// </para>
    /// </remarks>
    private static bool TryGetDesignTimeExpression(
        ISymbol containingSymbol, [MaybeNullWhen(false)] out IPropertySymbol expression)
    {
        expression = null!;

        if (containingSymbol is not IMethodSymbol
            {
                MethodKind: MethodKind.PropertyGet,
                AssociatedSymbol: IPropertySymbol { IsOverride: true } property,
            })
        {
            return false;
        }

        // Asked of the base rather than compared against a literal, so the compiler still carries no
        // "Body"/"Chrome" spelling of its own. The name the override answers is the base's by C#'s rule,
        // which is why one property is enough to report with and no second out parameter is needed. A
        // type that inherits no BlazorCodeFirst base answers null here, and no property is named null.
        if (property.Name != DesignTimeBaseFacts.FindDesignTimeExpressionName(property.ContainingType))
            return false;

        expression = property;
        return true;
    }

    // ---------------------------------------------------------------------------
    // Helpers, deferred event handler context detection
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="mutation"/> is enclosed, at any nesting depth,
    /// by an anonymous function that is a recognized deferred handler argument. Every enclosing one is
    /// checked (not just the innermost), so a mutation inside a nested lambda that itself lives inside a
    /// recognized handler lambda (e.g. <c>OnClick(async () => items.ForEach(i => total += i))</c>) is
    /// still exempt. If-content lambdas and other non-handler lambdas do not match and analysis continues
    /// outward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk is on the operation tree, where <see cref="IAnonymousFunctionOperation"/> covers both
    /// spellings C# has for writing a handler inline: the compiler has already erased the difference
    /// between a lambda and <c>delegate(T v) { … }</c>, so there is no enumeration of syntax kinds to be
    /// kept complete. On the syntax tree there was, and naming one of the two — <c>LambdaExpressionSyntax</c>,
    /// whose sibling rather than subtype is <c>AnonymousMethodExpressionSyntax</c> — let the walk pass a
    /// deferred handler written with <c>delegate</c> without ever asking the classification about it
    /// (#209, #216).
    /// </para>
    /// <para>
    /// It is bounded by construction, where the syntax walk was not: an operation tree is rooted at the
    /// accessor body containing the mutation, so a mutation that is not in a handler stops there instead of
    /// climbing on through the class and the namespace to the compilation unit (#215). A method group
    /// handler is outside the walk, but harmlessly: its body is another member, which the analyzer visits
    /// on its own terms.
    /// </para>
    /// </remarks>
    private static bool IsInsideDeferredEventHandler(IOperation mutation, KnownSymbols knownSymbols)
    {
        for (var operation = mutation.Parent; operation is not null; operation = operation.Parent)
        {
            if (operation is IAnonymousFunctionOperation anonymousFunction &&
                IsDeferredHandlerArgument(anonymousFunction, knownSymbols))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="anonymousFunction"/> is a deferred handler
    /// argument: the handler of an event decoration (a named event shortcut such as <c>.OnClick(...)</c>,
    /// or <c>.On(...)</c>), the setter of a two-way <c>.Bind(...)</c> — the element decoration
    /// <c>Decorations.Bind(...)</c> or the component decoration
    /// <c>ComponentView&lt;TComponent&gt;.Bind(...)</c> — a capture action, the value of a component's
    /// <c>.Param(...)</c>, or the callback of an <c>EventCallback.Factory.Create</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One predicate over the classification rather than one per decoration group: which argument counts as
    /// the deferred one is the only thing that differs between them, and both groups reach it the same way
    /// — unwrap the anonymous function to its argument, read the enclosing invocation, ask
    /// <see cref="KnownSymbols.ClassifySurfaceMethod"/>. Two predicates meant walking the same chain twice
    /// whenever the first declined, and two copies of that prologue to keep in step.
    /// </para>
    /// <para>
    /// Each arm identifies its argument by the parameter it binds to, never by argument position: a named
    /// argument (<c>.On(handler: h, eventName: "onclick")</c>) can put the handler anywhere in the list.
    /// The <c>.Bind</c> getter is deliberately not a deferred position — it is evaluated while the frames
    /// are built, so a mutation there must still be reported — and neither is the component spelling's
    /// selector, which names a parameter rather than carrying a value. <see cref="BindParameters.IsSetter"/>
    /// separates the setter from both, by position rather than by delegate shape (#206).
    /// </para>
    /// <para>
    /// The event arm asks <see cref="EventParameters.IsHandler"/> for the same kind of answer, and for the
    /// same kind of reason. It used to exempt whichever argument was delegate-typed, which is a rule that
    /// selects every delegate argument and not the handler; an <c>.On</c> overload carrying a second one
    /// would have been exempted in the wrong place, with nothing anywhere holding the readers of that
    /// question together (#221).
    /// </para>
    /// <para>
    /// Both arms answer <see langword="false"/> for a decoration whose shape the classification recognizes
    /// but whose argument roles it cannot name, and that is the one place this analyzer knowingly spends
    /// the asymmetry stated at the top of this file: it reports rather than stays quiet, so the cost is a
    /// spurious BCF3001 on a correct handler or setter. It is spendable only because the shape cannot reach
    /// an author. <c>KnownSymbolsSyncTests</c> asks
    /// <see cref="KnownSymbols.TryGetEventParameters"/> of every event decoration the runtime declares, so
    /// declaring one outside the readable shape is red in the compiler's own suite first, with the decision
    /// to be made named in the failure.
    /// </para>
    /// <para>
    /// The chain below is read off the operation tree, which models the whole of it. Unwrapping it
    /// syntactically still had to cross into the operation tree for the last step — which parameter an
    /// argument binds to is not a syntactic fact — and paid for the crossing with a syntax-identity
    /// comparison that needed a paragraph to justify (#216).
    /// </para>
    /// <para>
    /// The <see cref="IDelegateCreationOperation"/> is required rather than tolerated. It is how an
    /// anonymous function reaches a delegate-typed parameter, and it is present for every spelling the
    /// surface accepts — lambda, <c>delegate</c>, an explicit cast to the delegate type, a null-forgiving
    /// suppression, a named argument — because the cast and the suppression are elided from the tree
    /// rather than modelled above the anonymous function. Shapes that do differ add a node <em>above</em>
    /// the delegate creation, never in place of it: an <c>IConversionOperation</c> for a
    /// <c>Delegate</c>- or <c>object</c>-typed parameter, an array or collection-expression node for a
    /// <c>params</c> one. Requiring the node rejects those at a named place, which is what should happen
    /// until a surface parameter is declared in one of those shapes and the exemption is decided for it.
    /// </para>
    /// </remarks>
    private static bool IsDeferredHandlerArgument(
        IAnonymousFunctionOperation anonymousFunction, KnownSymbols knownSymbols)
    {
        if (anonymousFunction.Parent
            is not IDelegateCreationOperation
            {
                Parent: IArgumentOperation { Parent: IInvocationOperation invocation } argument,
            })
        {
            return false;
        }

        return knownSymbols.ClassifySurfaceMethod(invocation.TargetMethod) switch
        {
            SurfaceMethodKind.EventShortcut or SurfaceMethodKind.On =>
                KnownSymbols.TryGetEventParameters(invocation.TargetMethod, out var eventParameters)
                    && eventParameters.IsHandler(argument.Parameter),
            SurfaceMethodKind.Bind or SurfaceMethodKind.ComponentBind =>
                KnownSymbols.TryGetBindParameters(invocation.TargetMethod, out var bind)
                    && bind.IsSetter(argument.Parameter),
            // A component parameter's value. The delegate written there is handed to the child, which
            // invokes it when it raises the callback, so nothing invokes it while the parent's frames are
            // built — the child-to-parent callback is the most common thing this channel carries (#385).
            // The value is the second parameter, and it is there to be indexed: ScalarParam is classified
            // from the parameter list itself, which answers None for any arity but two. The selector
            // beside it names a parameter rather than carrying one, exactly as the component .Bind
            // selector does, so a mutation written there is reported.
            SurfaceMethodKind.ScalarParam =>
                SymbolEqualityComparer.Default.Equals(
                    argument.Parameter, invocation.TargetMethod.Parameters[1]),
            // A capture action runs when the captured reference changes, which is after the frames are
            // built, so assigning the captured value is deferred exactly as a handler's mutation is — and
            // it is the only thing the channel exists to do. No KnownSymbols reader answers which
            // parameter carries it, unlike the two shapes above where the delegate sits among strings:
            // the capture is the decoration's last parameter, and comparing against it rather than
            // accepting any argument keeps this arm honest if an overload ever adds one after it.
            SurfaceMethodKind.Ref or SurfaceMethodKind.ComponentRef =>
                invocation.TargetMethod.Parameters.Length > 0
                    && SymbolEqualityComparer.Default.Equals(
                        argument.Parameter,
                        invocation.TargetMethod.Parameters[invocation.TargetMethod.Parameters.Length - 1]),
            // Not a surface method, which is where the framework's own spelling of a handler lands:
            // .Param(c => c.OnPicked, EventCallback.Factory.Create(this, () => _count++)) reaches here
            // with Create in hand, and no arm above can match a call this surface does not declare. What
            // the factory does with the delegate is store it, so the callback runs when the child raises
            // the event, wherever the EventCallback it returns is written — which is why this asks about
            // the call alone rather than walking back to the .Param value it is almost always written in
            // (#385). Asked in this arm rather than ahead of the switch so a handler on a decoration is
            // still answered by the classification alone, and never forces the factory's own lookup.
            SurfaceMethodKind.None =>
                knownSymbols.IsEventCallbackFactoryMethod(invocation.TargetMethod),
            _ => false,
        };
    }
}

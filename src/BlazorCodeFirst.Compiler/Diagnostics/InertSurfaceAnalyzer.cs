using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCodeFirst.Compiler.Diagnostics;

/// <summary>
/// Reports BCF3029 when the design-time API is written where no design-time expression reads it, so the
/// expression builds an empty marker, renders nothing, and wires up no event handler.
/// </summary>
/// <remarks>
/// <para>
/// The inert-<c>View</c> model is what buys the rest of the design — no runtime tree, no reflection, no
/// allocation — and its one exposed edge is that the same API is callable from anywhere and means nothing
/// there. This closes that edge (#68).
/// </para>
/// <para>
/// An analyzer rather than a generator diagnostic, and separate from <see cref="RenderMutationAnalyzer"/>.
/// The shape compiles, so 付録A A.0's prohibition does not apply and the analyzer driver runs; BCF3001 is
/// the precedent for that. It stays its own type because BCF3001 looks only <em>inside</em> a design-time
/// expression while this looks only <em>outside</em> one, and two opposite range tests in a single type
/// leave it unreadable which condition a later change touched.
/// </para>
/// <para>
/// The registration is per operation rather than syntax-wide, and the first conjunct is a type test rather
/// than the name prefilter #68 planned. <see cref="OperationKind.Invocation"/> and
/// <see cref="OperationKind.PropertyReference"/> are between them every shape the surface is written in — a
/// helper property (<c>Html.Div</c>), a decoration or structural call (<c>.Class(…)</c>, <c>Html.If(…)</c>,
/// <c>Component&lt;T&gt;()</c>, a <c>[ViewPart]</c> call), and a bracketed child list, which is an indexer
/// — and <see cref="KnownSymbols.IsInertDesignTimeType"/> rejects all but a handful of them in at most four
/// reference comparisons, with no string hashed and no table consulted.
/// </para>
/// <para>
/// That choice was measured rather than argued, which is what #68 required before the registration was
/// fixed, and what the measurement showed is that the question was about the wrong thing: a no-op analyzer
/// registering these two operation kinds and a no-op analyzer registering the syntax-wide alternative cost
/// the same as each other. The breadth of the registration is free, so the prefilter would have narrowed the
/// free half. What costs is the callback, which is why the order of the conjuncts below is where the care
/// went. The figures and the method are on #68 rather than here, times being machine-dependent.
/// </para>
/// <para>
/// A per-block shortcut is not available, tempting as it looks: the enclosing declaration's return type
/// cannot be asked once per operation block and reused, because a lambda inside an inert-returning
/// declaration is its own declaration with its own return type. The motivating mistake is exactly that
/// shape — design-time syntax inside an <c>OnClick</c> handler written in a <c>Body</c> — so an early exit
/// on the block's own symbol would drop the case the diagnostic exists for.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InertSurfaceAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.BCF3029];

    public override void Initialize(AnalysisContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        // The emitted RenderView is generated code and calls none of the surface — it writes
        // builder.OpenElement and its neighbours — so excluding it costs no coverage and spares the
        // analyzer a file it can never report in.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static startContext =>
        {
            // Nothing is analyzed when the surface does not resolve, which happens when
            // BlazorCodeFirst.Html is absent or ambiguous between two references. Neither leaves any way to
            // tell a design-time expression from an ordinary method, so silence is the right degradation:
            // the cost of the alternative is an Error on correct code.
            if (KnownSymbols.TryCreate(startContext.Compilation) is not { } knownSymbols)
                return;

            startContext.RegisterOperationAction(
                operationContext => AnalyzeReference(operationContext, knownSymbols),
                OperationKind.Invocation,
                OperationKind.PropertyReference);
        });
    }

    // ---------------------------------------------------------------------------
    // Core analysis
    // ---------------------------------------------------------------------------

    private static void AnalyzeReference(OperationAnalysisContext ctx, KnownSymbols knownSymbols)
    {
        var operation = ctx.Operation;

        // Is this the surface at all. The member half is what keeps a declaration of the author's own out —
        // one that merely returns an inert type is the Opaque path DESIGN.md §5.3 reserves, and reporting a
        // call to one would erase a spelling 付録B.11(b) refuses to close.
        if (!IsDesignTimeApiReference(operation, knownSymbols))
            return;

        // One climb answering two questions: whether something enclosing this expression is a design-time
        // expression too (in which case that one is the report), and what the innermost enclosing declaration
        // returns. Asked before the storage test because it is the exemption that fires most — every
        // reference in a written chain but the outermost is exempted by its immediate parent, one iteration
        // in. The two are ORed, so their order cannot change the answer.
        if (IsReadByAnEnclosingDesignTimeExpression(operation, ctx.ContainingSymbol, knownSymbols))
            return;

        // Storage into an inert-typed field or property, which ARCHITECTURE.md §2.3 leaves unreserved.
        if (IsStoredInInertMember(operation, knownSymbols))
            return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.BCF3029, operation.Syntax.GetLocation()));
    }

    /// <summary>
    /// The member the operation references: the invoked method, or the property read. Never the reduced
    /// spelling of an extension method — <see cref="KnownSymbols.Normalize(IMethodSymbol)"/> handles that,
    /// and <see cref="IInvocationOperation.TargetMethod"/> is unreduced in any case.
    /// </summary>
    private static ISymbol? ReferencedMember(IOperation operation) => operation switch
    {
        IInvocationOperation invocation => invocation.TargetMethod,
        IPropertyReferenceOperation reference => reference.Property,
        _ => null,
    };

    /// <summary>
    /// Whether <paramref name="operation"/> is a reference to the design-time API, the two-conjunct test the
    /// report rests on and the climb re-asks of every enclosing expression.
    /// </summary>
    /// <remarks>
    /// The kind test comes first, and the climb is why. Registration already guarantees the kind at the entry
    /// point, so there the order is a wash; on the climb the ancestor is routinely neither an invocation nor a
    /// property reference, and an <see cref="IConversionOperation"/> in particular carries its <em>target</em>
    /// type — so a conversion to <c>View</c>, which sits on most children of a bracketed list, would pass a
    /// leading type test and be rejected only afterwards. Two type tests reject it before any symbol
    /// comparison, which is #220's rule (cheapest and most selective first) applied to the caller that
    /// actually has a choice.
    /// </remarks>
    private static bool IsDesignTimeApiReference(IOperation operation, KnownSymbols knownSymbols) =>
        ReferencedMember(operation) is { } member
        && knownSymbols.IsDesignTimeApiReference(operation.Type, member);

    // ---------------------------------------------------------------------------
    // Condition 2: storage into an inert-typed member
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Whether the value is being stored into a field or property whose own type is inert, either by
    /// assignment or by an initializer on the declaration itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both spellings, because which one an author reached for is not a distinction the design draws:
    /// <c>private View _cached = Html.Div["x"];</c> and a <c>_cached = …</c> in a method store the same
    /// value in the same place. The initializer arm is also the only thing that exempts one at all — a field
    /// initializer has no enclosing declaration with a return type, so condition 1 never reaches it.
    /// </para>
    /// <para>
    /// The <em>target's</em> type is what is asked, not the value's. The value is inert by the time this is
    /// reached, and an <c>object</c>-typed field accepting one through boxing is not the storage ARCHITECTURE.md §2.3 leaves
    /// open: nothing can read a <c>View</c> back out of it.
    /// </para>
    /// </remarks>
    private static bool IsStoredInInertMember(IOperation operation, KnownSymbols knownSymbols)
    {
        // A conversion sits between the value and its destination whenever the two types differ, so it is
        // skipped rather than required. Parentheses need no arm: C# does not model them in the operation
        // tree, so `(Html.Div["x"])`'s parent is already the destination.
        var value = operation;
        while (value.Parent is IConversionOperation)
            value = value.Parent;

        return value.Parent switch
        {
            // One arm for both member kinds: a field or property reference expression carries the member's
            // own type as its own. The kind test stays load-bearing all the same — reduced to a bare
            // Target.Type it would exempt an assignment to a View-typed local, which is reported.
            ISimpleAssignmentOperation
            {
                Target: IFieldReferenceOperation or IPropertyReferenceOperation,
            } assignment => knownSymbols.IsInertDesignTimeType(assignment.Target.Type),

            // The two initializer arms cannot merge the same way: ISymbolInitializerOperation exposes no
            // common accessor for the symbols being initialized.
            IFieldInitializerOperation { InitializedFields.Length: > 0 } initializer =>
                knownSymbols.IsInertDesignTimeType(initializer.InitializedFields[0].Type),
            IPropertyInitializerOperation { InitializedProperties.Length: > 0 } initializer =>
                knownSymbols.IsInertDesignTimeType(initializer.InitializedProperties[0].Type),
            _ => false,
        };
    }

    // ---------------------------------------------------------------------------
    // Condition 1, and the one-report-per-expression rule
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Whether this expression is read by a design-time expression after all: either it is part of a larger
    /// one, which is what gets reported instead, or the innermost declaration enclosing it returns an inert
    /// type, which is what <c>Body</c>, <c>Chrome</c>, a <c>[ViewPart]</c> body, and a content lambda all
    /// have in common.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two questions share a climb because they share a stopping point. A written chain such as
    /// <c>Html.Div.Class("card")[Html.Span["hello"]]</c> is five design-time references, and reporting each
    /// would give an author five errors for one mistake; the outermost is the one whose position is wrong, so
    /// an inner reference stays silent whenever something enclosing it is one too. That test has to stop at
    /// the first enclosing lambda or local function, and for a reason rather than for tidiness: the
    /// decoration call in <c>Html.Button.OnClick(() =&gt; { var v = Html.Div["x"]; })</c> is inert and does
    /// enclose the handler body, but the handler is runtime code and design-time syntax written in it is
    /// precisely the mistake. That boundary is the same one condition 1 asks its question at, so one loop
    /// answers both.
    /// </para>
    /// <para>
    /// Condition 1 is a declaration's return type rather than an allow-list of positions, which is what lets
    /// <c>Body</c>, <c>Chrome</c> and every <c>[ViewPart]</c> be exempt without any of them being named
    /// here: all three return an inert type, and so does the content lambda of an <c>If</c> or a
    /// <c>ForEach</c>. Nothing has to be added when a new inert-returning position is designed, and nothing
    /// silently loses its exemption when one is renamed.
    /// </para>
    /// <para>
    /// A field or property initializer reaches the end of the climb with no lambda found and a
    /// non-<see cref="IMethodSymbol"/> containing symbol, so it is not exempt here. That is deliberate:
    /// <see cref="IsStoredInInertMember"/> is what decides an initializer, on the declared type it
    /// initializes, and an initializer of some other type is a real report.
    /// </para>
    /// </remarks>
    private static bool IsReadByAnEnclosingDesignTimeExpression(
        IOperation operation, ISymbol containingSymbol, KnownSymbols knownSymbols)
    {
        for (var ancestor = operation.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                // The innermost enclosing declaration, and the end of the climb either way.
                case IAnonymousFunctionOperation { Symbol: { } lambda }:
                    return knownSymbols.IsInertDesignTimeType(lambda.ReturnType);
                case ILocalFunctionOperation { Symbol: { } local }:
                    return knownSymbols.IsInertDesignTimeType(local.ReturnType);
            }

            // Part of a larger design-time expression, which owns the report. The full two-conjunct test and
            // not merely an inert type: an assignment, a discard, and a variable initializer all carry the
            // type of the value assigned through them, so a type test alone would read `_ = Html.Div["x"]`
            // as enclosed by something inert and report nothing at all.
            if (IsDesignTimeApiReference(ancestor, knownSymbols))
                return true;
        }

        // No lambda enclosed it, so the declaration the operation is analyzed under is the innermost one.
        // Roslyn hands that over as a symbol, which covers a method, a local function, a constructor, and a
        // property or indexer accessor — a getter's return type being the property's type, which is how
        // Body and Chrome answer here.
        return containingSymbol is IMethodSymbol method
            && knownSymbols.IsInertDesignTimeType(method.ReturnType);
    }
}

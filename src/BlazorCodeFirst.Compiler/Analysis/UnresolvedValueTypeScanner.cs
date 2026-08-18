using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Reports unresolved types only from expressions that the SSC analyzer would emit as dynamic values when
/// a surrounding invocation has no selected method symbol and normal analysis cannot reach that value.
/// </summary>
internal static class UnresolvedValueTypeScanner
{
    public static void Report(ExpressionSyntax root, ViewPartBodyContext context) =>
        ScanRenderExpression(root, context);

    private static void ScanRenderExpression(ExpressionSyntax? expression, ViewPartBodyContext context)
    {
        if (expression is null)
            return;

        if (IsTextOrRenderFragment(expression, context))
        {
            ReportValue(expression, context);
            return;
        }

        // Children written in brackets. This has to come before the invocation guard below, which used to
        // be the first thing here: with children in brackets the body's own root is an element access, so
        // returning at it took the entire sweep with it and every diagnostic below went silent.
        if (expression is ElementAccessExpressionSyntax elementAccess)
        {
            ScanChildrenIndexer(elementAccess, context);
            return;
        }

        if (expression is not InvocationExpressionSyntax invocation)
            return;

        var recognized = Recognize(invocation, context);
        if (!recognized.IsSurfaceCall)
            return;

        // Recognizing the call is the whole of what walking its receiver depends on, so the walk is stated
        // once, here, rather than by each arm that has a chain to climb. A receiver is the same expression
        // whichever overload was written (#197) and the same expression whichever arm reads the arguments,
        // and on the arms whose call is static it is a type name the walk returns from at once. Reading the
        // arguments is what still needs the overload, because only the overload says what each of them
        // means. ScanChildrenIndexer takes the same shape for the same reason.
        ScanRenderExpression(Receiver(invocation), context);

        // A recognized call this scanner could not read through: its overload group named no single
        // overload, or the arguments did not bind to the one it named. The receiver is already walked, so
        // only the arguments go unread.
        if (recognized.Method is not { } method
            || (recognized.Arguments ?? BindArguments(invocation, method, context)) is not { } args)
        {
            return;
        }

        var recoverOwnValue = context.ShouldRecoverUnresolvedValue(invocation.Span);

        // One arm per SurfaceMethodKind, dispatching on the same lookup that recognized the call, so an
        // arm and its recognition cannot disagree the way the three predicate chains this replaced could
        // (#191, #201). A kind added to the enum without an arm here used to fall out of the switch and be
        // scanned as nothing, and the prompt this comment relied on — RenderExpressionAnalyzer.Classify
        // being a switch expression that stops compiling — is a prompt in another file and not a check
        // here. It was not enough: the five kinds #309/#310/#311 added were handled there and missed here.
        // The default arm below is the check, in the exhaustiveness contract RenderViewEmitter.EmitNode
        // and KeyabilityResolver.ResolveRoot already state for their own dispatches.
        var kind = context.KnownSymbols.ClassifySurfaceMethod(method);
        switch (kind)
        {
            // Element(tag) carries no children on this surface: they are written in brackets on the
            // ElementView it returns, and ScanChildrenIndexer handles that. The tag itself is never
            // reported on, whether or not it is constant. Component<T>() is the same shape twice over: it
            // takes no arguments at all, and its children likewise arrive on an indexer.
            case SurfaceMethodKind.Element:
            case SurfaceMethodKind.Component:
                return;

            case SurfaceMethodKind.If:
                ReportValue(args.At(0)?.Expression, context);
                ScanLambdaBody(args.At(1)?.Expression, context);
                ScanLambdaBody(args.At(2)?.Expression, context);
                return;

            case SurfaceMethodKind.ForEach:
                ReportValue(args.At(0)?.Expression, context);
                ReportLambdaValueBody(args.At(1)?.Expression, context);
                ScanLambdaBody(args.At(2)?.Expression, context);
                return;

            case SurfaceMethodKind.Raw:
                ReportValue(args.At(0)?.Expression, context);
                return;

            case SurfaceMethodKind.Fragment:
                ScanChildren(args, context);
                return;

            case SurfaceMethodKind.ScalarParam:
            case SurfaceMethodKind.FragmentParam:
            case SurfaceMethodKind.GenericTemplateIgnored:
            case SurfaceMethodKind.GenericTemplateContextual:
            case SurfaceMethodKind.ComponentBind:
                ScanComponentParameter(method, kind, args, recoverOwnValue, context);
                return;

            case SurfaceMethodKind.Class:
            case SurfaceMethodKind.AttributeShortcut:
            case SurfaceMethodKind.EventShortcut:
            case SurfaceMethodKind.Attr:
            case SurfaceMethodKind.On:
            case SurfaceMethodKind.Bind:
            // The non-attribute frame decorations reach ScanDecoration's tail, which reports argument 0:
            // both carry their one value there and neither has a name argument, exactly as .Class does.
            // A capture action is a lambda rather than a value, and that changes nothing — the event arm
            // reports its own handler with the same ReportValue, because what is scanned is the expression
            // transplanted into generated code.
            case SurfaceMethodKind.Key:
            case SurfaceMethodKind.Ref:
            // FormName reaches the same tail too: one value, no name argument, same as Key and Ref.
            case SurfaceMethodKind.FormName:
            // The event modifiers reach the same tail for the same reason, and their valueless overload
            // needs nothing extra: the tail reads argument 0 through a null-conditional, so the spelling
            // that writes no argument reports nothing rather than throwing (#368).
            case SurfaceMethodKind.PreventDefault:
            case SurfaceMethodKind.StopPropagation:
                ScanDecoration(invocation, method, kind, args, recoverOwnValue, context);
                return;

            // The component receivers of the same three channels. They take no selector, so they route
            // past ScanComponentParameter rather than through it, and their value is argument 0.
            case SurfaceMethodKind.ComponentKey:
            case SurfaceMethodKind.ComponentRenderMode:
            case SurfaceMethodKind.ComponentRef:
                if (recoverOwnValue)
                    ReportValue(args.At(0)?.Expression, context);

                return;

            // A [ViewPart] call. The walk above reaches the receiver of one written fluently, which is
            // the answer this arm wants: a receiver is an expression the author wrote, and this arm reports
            // on what the author wrote. Such a call never expands — a view part is never an extension
            // member (DESIGN.md §4.3, #203) — so walking it can only ever replace the generic BCF1003 with
            // a specific report, never emit anything new.
            case SurfaceMethodKind.None:
                if (context.KnownSymbols.IsViewPart(method))
                {
                    foreach (var argument in args.ExplicitArguments)
                        ReportValue(argument, context);
                }

                return;

            default:
                throw new System.NotSupportedException(
                    $"Unknown SurfaceMethodKind '{kind}'; add a scan arm for it. A kind with no arm is "
                        + "scanned as nothing, which costs the author a pointed unresolved-type report and "
                        + "leaves only the blanket BCF1003.");
        }
    }

    /// <summary>
    /// Scans a <c>.Param</c>, <c>.Template</c> or <c>.Bind</c> written on a component. The three share a
    /// route, as they do in <c>RenderExpressionAnalyzer.Classify</c>: all of them chain off a
    /// <c>ComponentView&lt;T&gt;</c> receiver, which the caller has already walked, and all of them take
    /// the selector in the same position. Only what follows the selector differs.
    /// </summary>
    private static void ScanComponentParameter(
        IMethodSymbol method,
        SurfaceMethodKind kind,
        BoundArguments args,
        bool recoverOwnValue,
        ViewPartBodyContext context)
    {
        if (!recoverOwnValue)
            return;

        switch (kind)
        {
            // The selector is not a value position — it is read for the property it names and never
            // emitted, so a bad one is BCF3005's to report. That is the same split the element-level
            // .Bind arm makes, and both reach it through ReportBindArguments.
            case SurfaceMethodKind.ComponentBind:
                ReportBindArguments(method, args, context);
                return;

            case SurfaceMethodKind.ScalarParam:
                ReportValue(args.At(1)?.Expression, context);
                return;

            // Reaching this arm at all needs Method resolved for the .Template call itself, which needs
            // GetSymbolInfo to name a single overload of the two same-arity Template<TContext> spellings
            // (GenericTemplateIgnored's View content and this one's Func<TContext, View> content).
            // Measured across an invocation-shaped content body, an element-access-shaped one (not
            // statement-expression-compatible with the other overload's RenderFragment-conversion path),
            // a block-bodied one, and a body containing nothing but a bare unresolved typeof: every one
            // that carries a name this scan could report on also poisons GetSymbolInfo into handing back
            // both overloads as CandidateSymbols, regardless of body shape, because typing the lambda well
            // enough to eliminate either candidate is exactly what the unresolved name inside it defeats.
            // TrySelectCandidate then refuses the pair (their SurfaceMethodKinds differ), Method stays
            // null, and this arm never runs. The complementary case — content clean enough that Method
            // does resolve — reaches this line with nothing left to report. No construction found reaches
            // it with both at once; the same shape as L431/L467's equivalence, just via lambda-conversion
            // ambiguity instead of argument binding.
            case SurfaceMethodKind.GenericTemplateContextual:
                ScanLambdaBody(args.At(1)?.Expression, context);
                return;

            // .Param onto a RenderFragment slot and the .Template spelling that ignores its context: both
            // take a View, which is scanned as an expression of its own.
            default:
                ScanRenderExpression(args.At(1)?.Expression, context);
                return;
        }
    }

    /// <summary>
    /// Scans whichever of one element decoration's own arguments reaches generated code. The chain it is
    /// written onto, carrying the element and everything decorated before this call, is the receiver the
    /// caller has already walked.
    /// </summary>
    /// <remarks>
    /// A decoration spelled as a static call, <c>Decorations.Class(builder, "c")</c>, has no argument read
    /// here at all: its arguments sit one position further along than the fluent spelling this reads. Its
    /// receiver is a type name, so the caller's walk returns from it without reaching anything, and the
    /// element it decorates is written as that first argument, which this route deliberately leaves unread
    /// rather than reading at a second set of positions.
    /// </remarks>
    private static void ScanDecoration(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SurfaceMethodKind kind,
        BoundArguments args,
        bool recoverOwnValue,
        ViewPartBodyContext context)
    {
        if (!recoverOwnValue || !IsFluentExtensionInvocation(invocation, method, context))
            return;

        if (kind is SurfaceMethodKind.EventShortcut or SurfaceMethodKind.On)
        {
            ReportEventArguments(method, args, context);
            return;
        }

        if (kind == SurfaceMethodKind.Attr)
        {
            if (IsNonEmptyConstantString(args.At(0)?.Expression, context))
                ReportValue(args.At(1)?.Expression, context);
            else
                ReportSelectedInvocationValues(args.At(1)?.Expression, context);
            return;
        }

        if (kind == SurfaceMethodKind.Bind)
        {
            // The two name arguments are not value positions, so this reads only from the getter
            // onward and never reports on either name directly. A non-constant name is usually
            // BCF3011's to report, with that rejection already clearing recoverOwnValue by the time
            // this runs — but not always: an unselected-invocation sibling inside the name itself
            // (the same shape used throughout this file) can poison the normal walk's own
            // FactoryArguments.Bind before its constant check ever runs, leaving recoverOwnValue true
            // with a non-constant name still in argument 0 (measured,
            // BindNameSiblingOfUnselectedInvocation_UnresolvedType_DoesNotReportBCF3015). The name is
            // still never read here either way.
            ReportBindArguments(method, args, context);
            return;
        }

        // .Class and a named attribute shortcut each carry their one value in argument 0, the name being
        // the decoration's own spelling rather than something written.
        ReportValue(args.At(0)?.Expression, context);
    }

    /// <summary>
    /// Reports on an event decoration's handler argument, the one transplanted into generated code.
    /// </summary>
    /// <remarks>
    /// The event sibling of <see cref="ReportBindArguments"/>, and here for the same reason: the position
    /// is read off <see cref="KnownSymbols.TryGetEventParameters"/> rather than written as an ordinal, so
    /// this scan follows the overload set instead of having to be transcribed alongside it. #221 counted
    /// three readers of "which argument is the handler" and this file held a fourth, one arm away from the
    /// paragraph below stating the principle for <c>.Bind</c> — which is what a transcribed position looks
    /// like when nobody is holding the copies together.
    /// <para>
    /// The event's name is not a value position, so a non-constant one only changes <em>how</em> the
    /// handler is reported, exactly as the merged <c>.Attr</c> arm above still does for its own value.
    /// A named shortcut carries no name argument at all, which is what <c>CarriesEventName</c> answers.
    /// </para>
    /// <para>
    /// A shape this compiler was not written against reports nothing rather than reporting at a guessed
    /// position. The body is already BCF1003 by then, so the cost is a report not made, not a wrong one.
    /// </para>
    /// </remarks>
    private static void ReportEventArguments(
        IMethodSymbol method, BoundArguments args, ViewPartBodyContext context)
    {
        if (!KnownSymbols.TryGetEventParameters(method, out var eventParameters))
            return;

        var handler = args.At(eventParameters.HandlerIndex)?.Expression;

        if (eventParameters.CarriesEventName
            && !IsNonEmptyConstantString(args.At(eventParameters.EventNameIndex)?.Expression, context))
        {
            ReportSelectedInvocationValues(handler, context);
            return;
        }

        ReportValue(handler, context);
    }

    /// <summary>
    /// Reports on a binding's getter and every argument written after it: the arguments transplanted into
    /// generated code, and therefore the ones that can name a type that does not resolve.
    /// </summary>
    /// <remarks>
    /// One body for both surfaces, taking the getter's position off
    /// <see cref="KnownSymbols.TryGetBindParameters"/> rather than from an ordinal written here, so this
    /// scan follows the overload set instead of having to be transcribed alongside it (#206).
    /// <para>
    /// Everything from the getter onward is reported, rather than the getter and the setter by name. Each
    /// of those positions — the setter, the format, the culture — is transplanted whole into the
    /// generated file, so a type that does not resolve in any of them is the same defect, and no
    /// enumeration of roles has to be kept in step with the overload set. What sits <em>ahead</em> of the
    /// getter is never a value position on either surface: the element spelling writes the attribute and
    /// event names there, which are constants BCF3011 owns, and the component spelling writes the
    /// selector, which names a property and is BCF3005's (#307).
    /// </para>
    /// <para>
    /// Reporting the union is also what lets <see cref="AreInterchangeableOverloads"/> accept two
    /// bindings whose fourth argument means different things. Before #307 that argument was always the
    /// setter; a <c>CultureInfo</c> now sits in the same place, and refusing the pair cost BCF3015 the
    /// case it exists for — a type only this generator cannot resolve, where the author's own build is
    /// green and the sole symptom would otherwise be a bare BCF1003.
    /// </para>
    /// <para>
    /// A method this compiler was not written against reports nothing rather than reporting at a guessed
    /// position. The body is already BCF1003 by then, so the cost is a report not made, not a wrong one.
    /// </para>
    /// </remarks>
    private static void ReportBindArguments(
        IMethodSymbol method, BoundArguments args, ViewPartBodyContext context)
    {
        if (!KnownSymbols.TryGetBindParameters(method, out var bind))
            return;

        var written = WrittenParameterCount(method);
        for (var index = bind.GetterIndex; index < written; index++)
            ReportValue(args.At(index)?.Expression, context);
    }

    /// <summary>
    /// Scans an element access whose indexer is one of the design-time surface's. Both indexers,
    /// <c>ElementView</c>'s and <c>ComponentView&lt;T&gt;</c>'s, take the same route: the receiver carries
    /// the tag, the decoration chain or the component parameter chain and is scanned as an expression in
    /// its own right, and the bracketed arguments are the children.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is gated on <see cref="ViewPartBodyContext.ShouldRecoverUnresolvedValue"/>. That gate
    /// exists so an arm does not re-report a value it already diagnosed, and this route has no value of its
    /// own: the tag belongs to the receiver, which carries its own gate. Gating the children on it would
    /// silence expressions the rejection was never about.
    /// </para>
    /// <para>
    /// The children are scanned only when the receiver's tag survives BCF3009. A non-constant tag has
    /// already rejected the whole element, so nothing inside the brackets reaches generated code and a
    /// report about it would be noise on top of the real error. The rule is not restated here: the gate
    /// asks the receiver, through <see cref="HasRejectedElementTag"/>, because the receiver is what owns
    /// the tag and what BCF3009 is reported on.
    /// </para>
    /// </remarks>
    private static void ScanChildrenIndexer(
        ElementAccessExpressionSyntax elementAccess, ViewPartBodyContext context)
    {
        if (TryGetRecognizedIndexer(elementAccess, context) is not { } indexer)
            return;

        ScanRenderExpression(elementAccess.Expression, context);

        // A non-constant tag has already been rejected by BCF3009 on the receiver, so the children never
        // reach generated code and reporting on them is noise. This is the gate the method-surface Element
        // arm carried before #87 deleted it.
        if (HasRejectedElementTag(elementAccess.Expression, context))
            return;

        if (BindIndexerArguments(elementAccess, indexer, context) is { } args)
            ScanChildren(args, context);
    }

    /// <summary>
    /// Whether <paramref name="receiver"/> resolves through its decoration chain to an
    /// <c>Element(tag)</c> call whose tag BCF3009 rejects, whether for not being a constant string or for
    /// not being spelled like a tag name.
    /// </summary>
    /// <remarks>
    /// The chain is unwound rather than inspected at the top, because decorations sit between the element
    /// helper and the brackets: in <c>Element(t).Class("c")["x"]</c> the indexer's receiver is the <c>Class</c>
    /// invocation. A curated tag is a property reference and never reaches the loop's body, so it returns
    /// false and its children are scanned as before.
    /// <para>
    /// Every path fails open: an unresolved symbol, an unrecognized method, a missing receiver and a tag
    /// that cannot be bound all answer false. This gate can only ever silence diagnostics, so a route it
    /// could not analyze must leave the children scanned; the alternative is losing a report on evidence
    /// that was never gathered.
    /// </para>
    /// </remarks>
    private static bool HasRejectedElementTag(
        ExpressionSyntax? receiver, ViewPartBodyContext context)
    {
        while (receiver is InvocationExpressionSyntax invocation)
        {
            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            {
                return false;
            }

            var kind = context.KnownSymbols.ClassifySurfaceMethod(method);
            if (kind == SurfaceMethodKind.Element)
            {
                // The tag has to be reached before it can be called non-constant. A binding failure is not
                // evidence of a rejected tag, and answering true on one would suppress the children's
                // diagnostics on nothing.
                return BindArguments(invocation, method, context)?.At(0)?.Expression is { } tag
                    && !ElementTag.TryResolve(tag, context, out _);
            }

            if (!IsElementDecoration(kind))
                return false;

            receiver = Receiver(invocation);
        }

        return false;
    }

    private static bool IsTextOrRenderFragment(ExpressionSyntax expression, ViewPartBodyContext context)
    {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        return type is { SpecialType: SpecialType.System_String }
            || (context.KnownSymbols.RenderFragmentType is { } renderFragment
                && SymbolEqualityComparer.Default.Equals(type, renderFragment));
    }

    private static void ScanChildren(BoundArguments args, ViewPartBodyContext context)
    {
        if (args.HasUnanalyzableParamsArgument)
            return;

        foreach (var child in args.ParamsElements)
        {
            if (child.IsSpread)
                ScanSplice(child.Expression, context);
            else
                ScanRenderExpression(child.Expression, context);
        }
    }

    /// <summary>
    /// A spliced child list (<c>.. source.Select(item =&gt; …)</c>): the selector's body is a render
    /// expression and is scanned as one. Scanning the whole call as a render expression reaches nothing,
    /// and a BCF3015 inside a spliced child goes unreported (#172).
    /// </summary>
    /// <remarks>
    /// The shape is recognized syntactically and the semantic check only <em>excludes</em>: a call that
    /// resolves to something other than <c>Enumerable.Select</c> is skipped, and one that resolves to
    /// nothing is scanned anyway. Requiring the symbol would make this blind exactly when it matters,
    /// because an unresolvable name inside the selector is what stops the call from binding in the first
    /// place — measured, and the reason this fails open like the element-tag gate above.
    /// <para>
    /// Reporting on a spread the analyzer would refuse costs nothing that is wrong. BCF3015 fires only on
    /// a name that genuinely does not resolve, and such a body is BCF1003 either way; what the report
    /// changes is that the author is told which name could not be resolved instead of only that the body
    /// could not be translated.
    /// </para>
    /// </remarks>
    private static void ScanSplice(ExpressionSyntax expression, ViewPartBodyContext context)
    {
        if (!SpliceSyntax.TryMatchProjection(expression, out var invocation, out _, out var selector))
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is IMethodSymbol method
            && !context.KnownSymbols.IsEnumerableSelect(method))
        {
            return;
        }

        ScanLambdaBody(selector, context);
    }

    private static void ScanLambdaBody(ExpressionSyntax? expression, ViewPartBodyContext context)
    {
        ScanRenderExpression(GetLambdaBody(expression), context);
    }

    private static void ReportLambdaValueBody(ExpressionSyntax? expression, ViewPartBodyContext context) =>
        ReportValue(GetLambdaBody(expression), context);

    private static ExpressionSyntax? GetLambdaBody(ExpressionSyntax? expression) =>
        expression switch
        {
            SimpleLambdaExpressionSyntax { Body: ExpressionSyntax simpleBody } => simpleBody,
            ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax parenthesizedBody } => parenthesizedBody,
            _ => null,
        };

    private static void ReportValue(ExpressionSyntax? expression, ViewPartBodyContext context)
    {
        if (expression is null)
            return;

        foreach (var name in expression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (IsInsideNameofConstant(name, context))
                continue;

            if (IsLambdaParameterDeclaration(name) || IsInsideUnselectedInvocation(name, context))
                continue;

            _ = ExpressionTemplateFactory.TryReportUnresolvedType(name, context);
        }
    }

    private static bool IsLambdaParameterDeclaration(SimpleNameSyntax name) =>
        name.AncestorsAndSelf().OfType<ParameterSyntax>().Any(parameter => parameter.Type?.Span.Contains(name.Span) == true);

    // An element access counts as much as an invocation: with children in brackets, an unresolvable call
    // enclosing a name is as likely to be spelled Div[…] as Div(…), and a name inside one is emitted by
    // neither.
    private static bool IsInsideUnselectedInvocation(SimpleNameSyntax name, ViewPartBodyContext context) =>
        name.Ancestors().Any(ancestor => ancestor switch
        {
            // Asked of the call, not of its overload: a recognized call whose overload could not be named
            // is one the sweep walks through, so a value under it is reached and must not be suppressed
            // here on the way out.
            // IsSplicedSelect is three syntax tests and IsSurfaceCall is a second GetSymbolInfo plus a
            // list allocation, so the free one is asked first. It is also the one that answers true in
            // the case it was added for, where the whole point is that nothing binds.
            InvocationExpressionSyntax invocation =>
                !IsSplicedSelect(invocation)
                    && context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is null
                    && !IsSurfaceCall(invocation, context),
            ElementAccessExpressionSyntax elementAccess =>
                context.SemanticModel.GetSymbolInfo(elementAccess, context.CancellationToken).Symbol is null
                    && TryGetRecognizedIndexer(elementAccess, context) is null,
            _ => false,
        });

    /// <summary>
    /// Whether <paramref name="invocation"/> is the <c>Select</c> of a spliced child list, the operand of
    /// a spread written directly in a child list (<c>Div[[.. src.Select(i =&gt; …)]]</c>).
    /// </summary>
    /// <remarks>
    /// The same exemption <see cref="IsSurfaceCall"/> earns, for the same reason: this is a call the sweep
    /// deliberately walks into, so a value under it is reached and must not be suppressed on the way out.
    /// It has to be named separately because a spliced <c>Select</c> is not part of the design-time
    /// surface — it is host-language code the surface reads.
    /// <para>
    /// Without it, the sugar and the spelling it is sugar for disagree, which was measured:
    /// <c>ForEach(items, key: null, content: i =&gt; Li[typeof(Probe).Name])</c> reports BCF3015 while
    /// <c>[.. items.Select(i =&gt; Li[typeof(Probe).Name])]</c> reported only BCF1003. The unresolved name
    /// is what stops <c>Select</c> from binding, so requiring a bound <c>Select</c> before looking inside
    /// it is blind in exactly the case worth looking.
    /// </para>
    /// <para>
    /// Asked of the syntax and not of a symbol, deliberately: there is no symbol in the case this exists
    /// for, which is why <see cref="SpliceSyntax"/> matches the name as written. The spread parent is
    /// what bounds it to a child list, so an ordinary <c>Select</c> written anywhere else keeps the
    /// suppression it had.
    /// </para>
    /// <para>
    /// Only the selector needs this. The splice's <em>source</em> is already reached by the ordinary
    /// sweep, measured: adding and removing a dedicated walk over it changed no input's diagnostics,
    /// including one with an unresolvable name in the source and another with one in both halves. A
    /// source broken enough not to be reached (<c>[.. Probe.Items.Select(…)]</c>) makes the whole element
    /// access an <c>IInvalidOperation</c>, so this scanner never runs on it and BCF1003 answers instead —
    /// the same measured condition <c>FactoryArguments</c> records.
    /// </para>
    /// </remarks>
    private static bool IsSplicedSelect(InvocationExpressionSyntax invocation) =>
        invocation.Parent is SpreadElementSyntax && SpliceSyntax.IsProjection(invocation);

    // The caller did not select a decoration route, but a deliberately invoked user method in its value
    // still has its own source expression and must retain diagnostics (notably an escaped @nameof method).
    // Do not walk arbitrary error invocations here: those have no selected symbol and are not emitted.
    private static void ReportSelectedInvocationValues(ExpressionSyntax? expression, ViewPartBodyContext context)
    {
        if (expression is null)
            return;

        foreach (var node in expression.DescendantNodesAndSelf())
        {
            // An indexer counts too, and its arguments are just as much its own source expressions: a
            // View-valued _dict["k"] in a decoration value is the bracket-surface analogue of the call this
            // was written for.
            var arguments = node switch
            {
                InvocationExpressionSyntax invocation
                    when context.SemanticModel.GetSymbolInfo(
                            invocation, context.CancellationToken).Symbol is IMethodSymbol
                        && !IsSurfaceCall(invocation, context) =>
                    invocation.ArgumentList.Arguments,
                ElementAccessExpressionSyntax elementAccess
                    when context.SemanticModel.GetSymbolInfo(
                            elementAccess, context.CancellationToken).Symbol is IPropertySymbol
                        && TryGetRecognizedIndexer(elementAccess, context) is null =>
                    elementAccess.ArgumentList.Arguments,
                _ => default,
            };

            foreach (var argument in arguments)
                ReportValue(argument.Expression, context);
        }
    }

    // Not extended to element access, unlike its neighbours: `nameof` is a contextual keyword invoked with
    // parentheses, so a nameof constant is always an InvocationExpressionSyntax and TryCreateNameofConstant
    // takes one.
    private static bool IsInsideNameofConstant(SimpleNameSyntax name, ViewPartBodyContext context) =>
        name.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            ExpressionTemplateFactory.TryCreateNameofConstant(invocation, context) is not null
                && invocation.ArgumentList.Span.Contains(name.Span));

    /// <summary>
    /// The name half of BCF3011, which stops at non-empty. The tag half is
    /// <see cref="ElementTag.TryResolve"/>, and the two are separate because the two diagnostics ask
    /// different questions: a <c>.Attr</c> name is not held to a tag name's spelling.
    /// </summary>
    private static bool IsNonEmptyConstantString(ExpressionSyntax? expression, ViewPartBodyContext context) =>
        expression is not null
        && context.SemanticModel.GetConstantValue(expression, context.CancellationToken) is
        { HasValue: true, Value: string value }
        && !string.IsNullOrWhiteSpace(value);

    private static BoundArguments? BindArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ViewPartBodyContext context)
    {
        if (!HasValidArgumentOrder(invocation, method, context))
            return null;

        if (FactoryArguments.Bind(invocation, context) is { } factoryArguments)
            return BoundArguments.FromFactory(factoryArguments, WrittenParameterCount(method));

        return BoundArguments.TryBindFallback(invocation, method);
    }

    /// <summary>
    /// Binds an indexer's bracketed arguments. The fallback binder matters more here than anywhere else:
    /// this scanner exists for compilations where symbol resolution failed, and the
    /// <see cref="FactoryArguments"/> overload requires an <c>IPropertyReferenceOperation</c>, which is
    /// exactly what is unavailable then.
    /// </summary>
    private static BoundArguments? BindIndexerArguments(
        ElementAccessExpressionSyntax elementAccess,
        IPropertySymbol indexer,
        ViewPartBodyContext context)
    {
        // An indexer is never an extension method, so the receiver offset is always 0.
        var parameters = indexer.Parameters;
        if (!HasValidArgumentOrder(elementAccess.ArgumentList, parameters, offset: 0))
            return null;

        if (FactoryArguments.Bind(elementAccess, context) is { } factoryArguments)
            return BoundArguments.FromFactory(factoryArguments, parameters.Length);

        return BoundArguments.TryBindFallback(elementAccess.ArgumentList, parameters, offset: 0);
    }

    private static bool HasValidArgumentOrder(
        InvocationExpressionSyntax invocation,
        IMethodSymbol selectedMethod,
        ViewPartBodyContext context)
    {
        // The whole declared list, receiver included, is what an argument list is checked against, because
        // the static spelling writes the receiver as an argument; only the fluent spelling skips it. That
        // condition is this site's own, and the receiver rule underneath it is KnownSymbols' (#211). The
        // offset is read first so the fluent test's semantic query is only paid by a method that has a
        // receiver to skip at all.
        var method = selectedMethod.ReducedFrom ?? selectedMethod;
        var receiverOffset = KnownSymbols.ReceiverOffset(method);
        var offset = receiverOffset != 0
            && IsFluentExtensionInvocation(invocation, selectedMethod, context)
                ? receiverOffset
                : 0;

        return HasValidArgumentOrder(invocation.ArgumentList, method.Parameters, offset);
    }

    private static bool HasValidArgumentOrder(
        BaseArgumentListSyntax argumentList,
        ImmutableArray<IParameterSymbol> parameters,
        int offset)
    {
        var parameterCount = parameters.Length - offset;
        // Mutating this to <= 0 is a stryker survivor, measured equivalent rather than assumed: a
        // zero-parameter call (Element, Component, or the valueless PreventDefault/StopPropagation
        // overload) does reach parameterCount == 0 (confirmed with a throwaway probe on
        // Div.Attr("x", MissingMethod() + typeof(Probe).Name).PreventDefault()), but returning false
        // there instead of falling through to an empty, vacuously-true loop makes no observable
        // difference. HasValidArgumentOrder failing only ever suppresses BindArguments, and every
        // reachable zero-parameter kind already reports nothing whether or not BindArguments runs:
        // Element/Component return before the switch's kind arms read anything, and the valueless
        // PreventDefault/StopPropagation spelling's arm reports args.At(0), which is null either way
        // for a zero-length bound-argument list. Reverting the mutant by hand and running both
        // BlazorCodeFirst.Compiler.Tests and BlazorCodeFirst.DiagnosticTests confirmed no test tells
        // the two apart.
        if (parameterCount < 0)
            return false;

        // C# allows a positional argument after a named argument only while each preceding name
        // occupied the next positional slot. Once a name reorders the arguments, all later arguments
        // must also be named.
        var nextPositional = 0;
        var hasOutOfPositionNamedArgument = false;

        foreach (var argument in argumentList.Arguments)
        {
            if (argument.NameColon is { } nameColon)
            {
                var index = FindParameter(parameters, offset, nameColon.Name.Identifier.ValueText);
                if (index < 0)
                    return false;

                if (index == nextPositional)
                    nextPositional++;
                else
                    hasOutOfPositionNamedArgument = true;

                continue;
            }

            if (hasOutOfPositionNamedArgument)
                return false;

            if ((uint)nextPositional < (uint)parameterCount
                && parameters[nextPositional + offset].IsParams)
            {
                continue;
            }

            nextPositional++;
        }

        return true;
    }

    private static int FindParameter(
        ImmutableArray<IParameterSymbol> parameters, int offset, string name)
    {
        for (var ordinal = offset; ordinal < parameters.Length; ordinal++)
        {
            if (parameters[ordinal].Name == name)
                return ordinal - offset;
        }

        return -1;
    }

    /// <summary>
    /// How many parameters of <paramref name="method"/> lie in argument space, which is the space
    /// <see cref="BoundArguments.At"/> indexes in.
    /// </summary>
    /// <remarks>
    /// No <c>ReducedFrom</c> step: <see cref="KnownSymbols.ReceiverOffset"/> answers 0 for a reduced
    /// method, whose parameter list already excludes the receiver, and 1 for an unreduced one, whose does
    /// not — so the count is the same either way and unreducing first would only be a second spelling of
    /// the same rule (#211).
    /// </remarks>
    private static int WrittenParameterCount(IMethodSymbol method) =>
        method.Parameters.Length - KnownSymbols.ReceiverOffset(method);

    /// <summary>
    /// Whether <paramref name="invocation"/> names a method of the design-time surface, asked without
    /// naming which overload. Selecting the overload costs a binding attempt against every candidate, and
    /// the answer to this question never depends on it: a group that named no overload is a surface call
    /// all the same.
    /// </summary>
    private static bool IsSurfaceCall(
        InvocationExpressionSyntax invocation, ViewPartBodyContext context) =>
        Recognize(invocation, context, selectOverload: false).IsSurfaceCall;

    /// <summary>
    /// What failure recovery can make of <paramref name="invocation"/>. Pass
    /// <paramref name="selectOverload"/> as <see langword="false"/> to leave
    /// <see cref="RecognizedInvocation.Method"/> unanswered, which callers that only ask
    /// <see cref="RecognizedInvocation.IsSurfaceCall"/> should do; see <see cref="IsSurfaceCall"/>.
    /// </summary>
    private static RecognizedInvocation Recognize(
        InvocationExpressionSyntax invocation,
        ViewPartBodyContext context,
        bool selectOverload = true)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method && IsRecognized(method, context))
            return RecognizedInvocation.Named(method);

        if (context.KnownSymbols.HtmlForEach is { } forEach
            && IsHtmlForEachInScope(invocation, forEach, context))
        {
            return RecognizedInvocation.Named(forEach);
        }

        var candidates = new List<IMethodSymbol>();
        foreach (var symbol in symbolInfo.CandidateSymbols)
            AddRecognizedCandidate(symbol, candidates, context);

        var expressionInfo = context.SemanticModel.GetSymbolInfo(invocation.Expression, context.CancellationToken);
        if (expressionInfo.Symbol is IMethodSymbol expressionMethod)
            AddRecognizedCandidate(expressionMethod, candidates, context);

        foreach (var symbol in expressionInfo.CandidateSymbols)
            AddRecognizedCandidate(symbol, candidates, context);

        // The name is looked up only when nothing else offered a candidate. A second HtmlForEach test used
        // to sit after this, and could never answer differently from the one above: nothing between them
        // reaches anything the test reads.
        if (candidates.Count == 0 && invocation.Expression is SimpleNameSyntax invocationName)
        {
            foreach (var symbol in context.SemanticModel.LookupSymbols(
                         invocation.SpanStart,
                         name: invocationName.Identifier.ValueText,
                         includeReducedExtensionMethods: true))
            {
                AddRecognizedCandidate(symbol, candidates, context);
            }
        }

        if (candidates.Count == 0)
            return RecognizedInvocation.None;

        return selectOverload
            ? TrySelectCandidate(invocation, candidates, context)
            : RecognizedInvocation.FromGroup(selected: null, arguments: null);
    }

    /// <summary>
    /// What failure recovery could make of an invocation: whether it names a method of the design-time
    /// surface at all, and which overload of it, where one could be named.
    /// </summary>
    /// <remarks>
    /// The two answers are separate because they gate different things. Reading an argument needs the
    /// overload, since only the parameter it landed on says what the argument means. Walking the receiver
    /// needs no more than the first answer: a receiver is the same expression whichever overload was
    /// written, so a call that is recognized but unselectable must still be walked through rather than
    /// abandoned (#197).
    /// </remarks>
    private readonly struct RecognizedInvocation
    {
        private RecognizedInvocation(
            IMethodSymbol? method, BoundArguments? arguments, bool isSurfaceCall)
        {
            Method = method;
            Arguments = arguments;
            IsSurfaceCall = isSurfaceCall;
        }

        /// <summary>Not a call this scanner knows: none of the answers is available.</summary>
        public static RecognizedInvocation None => default;

        /// <summary>The overload this call selects, or <see langword="null"/> when none could be named.</summary>
        public IMethodSymbol? Method { get; }

        /// <summary>
        /// <see cref="Method"/>'s arguments where naming it already bound them, so that the caller does
        /// not bind the same call a second time. Null whenever the overload was named without binding.
        /// </summary>
        public BoundArguments? Arguments { get; }

        /// <summary>
        /// Whether the call names a surface method, whether or not one overload of it could be named.
        /// </summary>
        public bool IsSurfaceCall { get; }

        public static RecognizedInvocation Named(IMethodSymbol method) =>
            new(method, arguments: null, isSurfaceCall: true);

        /// <summary>
        /// The answer for a non-empty group of recognized candidates: the overload
        /// <see cref="TrySelectCandidate"/> named and the arguments it bound to reach that answer, both
        /// null when it named none or was never asked. Either way the call is a surface call.
        /// </summary>
        public static RecognizedInvocation FromGroup(IMethodSymbol? selected, BoundArguments? arguments) =>
            new(selected, arguments, isSurfaceCall: true);
    }

    /// <summary>
    /// The one candidate in <paramref name="candidates"/> that the call's written arguments select, with
    /// the arguments bound to it, or neither when the group leaves the choice open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single candidate is returned as it stands. Beyond that, an unresolved type inside a <c>.Bind</c>
    /// argument is what makes the invocation fail to bind, and failed overload resolution hands back the
    /// whole overload group — twelve for <c>Decorations.Bind</c>, three for
    /// <c>ComponentView&lt;T&gt;.Bind</c> — including the ones no argument count could have selected.
    /// Refusing the group outright is what left BCF3015 unreachable on both <c>.Bind</c> surfaces (#197).
    /// </para>
    /// <para>
    /// So the survivors are those the written arguments fill exactly, which keeps a longer overload from
    /// answering a shorter call. Filling is not on its own enough to name one: the element surface declares
    /// its getter-only <c>.Bind</c> three times, once per bindable shape, and four written arguments fill
    /// five overloads. Those survivors are accepted when they agree, and refuse when they do not.
    /// </para>
    /// <para>
    /// What agreement means is the arm's question, not a property of the declarations. The test used to be
    /// stated as prefix alignment — an argument position meaning the same thing across every overload of
    /// one method — and #307 broke that: the fourth argument of a <c>.Bind</c> is a setter in four
    /// survivors and a <c>CultureInfo</c> in the fifth. Refusing on that would have cost BCF3015 the case
    /// it exists for, a type only this generator cannot resolve, where the author's own build is green.
    /// The survivors do agree about what is <em>scanned</em>, because
    /// <see cref="ReportBindArguments"/> reads the getter and every argument after it without reading a
    /// role, and every one of those is transplanted whichever role it holds. So
    /// <see cref="AreInterchangeableOverloads"/> asks that of a binding and keeps the declaration-shape
    /// proxy for the arms that do read raw ordinals.
    /// </para>
    /// <para>
    /// The refusing half is still untested, and deliberately so rather than by oversight. Every recognized
    /// name resolves to a single method and no arm that reads raw ordinals has an overload group whose
    /// positions disagree, so no call site can currently be written whose reading changes with the survivor
    /// picked; answering the first candidate that merely binds passes the whole suite. What the check buys
    /// is that a future overload breaking an arm's agreement costs a refused diagnostic rather than a
    /// silently misread one, which is the trade #197 asked for. A later reader finding it untested should
    /// weigh it on that, not take the absent test for a gap to fill.
    /// </para>
    /// </remarks>
    private static RecognizedInvocation TrySelectCandidate(
        InvocationExpressionSyntax invocation,
        List<IMethodSymbol> candidates,
        ViewPartBodyContext context)
    {
        if (candidates.Count == 1)
            return RecognizedInvocation.FromGroup(candidates[0], arguments: null);

        IMethodSymbol? selected = null;
        BoundArguments? selectedArguments = null;

        foreach (var candidate in candidates)
        {
            if (BindArguments(invocation, candidate, context) is not { } args
                || !FillsEveryParameter(args, candidate))
            {
                continue;
            }

            if (selected is null)
            {
                // Kept rather than rebound by the caller: deciding this candidate fills the call is the
                // same work as reading its arguments, and this route runs where every overload of a
                // six-way group has to be tried before one is named.
                selected = candidate;
                selectedArguments = args;
            }
            else if (!AreInterchangeableOverloads(selected, candidate, context.KnownSymbols))
            {
                return RecognizedInvocation.FromGroup(selected: null, arguments: null);
            }
        }

        return RecognizedInvocation.FromGroup(selected, selectedArguments);
    }

    /// <summary>
    /// Whether every parameter <paramref name="method"/> declares that the author had to write carries an
    /// argument. A <c>params</c> parameter is exempt, its elements living outside
    /// <see cref="BoundArguments.At"/>, and so is an optional one: leaving either unwritten is a call the
    /// author made, not an unfilled slot. <c>Html.If</c> is the optional case — its <c>otherwise</c> is
    /// omitted far more often than it is written.
    /// </summary>
    private static bool FillsEveryParameter(BoundArguments args, IMethodSymbol method)
    {
        var offset = KnownSymbols.ReceiverOffset(method);

        for (var index = 0; index < method.Parameters.Length - offset; index++)
        {
            var parameter = method.Parameters[index + offset];
            if (!parameter.IsParams && !parameter.IsOptional && !args.HasArgumentAt(index))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="left"/> and <paramref name="right"/> reach the same arm and bind arguments
    /// the same way, which together are the whole of what this scanner reads of a candidate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arm is asked for directly, as the classification it dispatches on. This used to be inferred
    /// from the declaration — same name, same containing type, same extension-ness — on the ground that
    /// two overloads of one surface method reach one arm by construction; the name half of that could
    /// never fire, since every source feeding the candidate list yields symbols sharing one identifier,
    /// and the containing type was what actually separated <c>Decorations.Bind</c> from
    /// <c>ComponentView&lt;T&gt;.Bind</c>. A <see cref="SurfaceMethodKind"/> answers both at once and
    /// keeps <c>.Class</c> apart from an attribute shortcut, which reading the arm's argument positions
    /// alone would not.
    /// </para>
    /// <para>
    /// Two <c>[ViewPart]</c> candidates both classify as <see cref="SurfaceMethodKind.None"/> and are
    /// interchangeable in fact as well as by this test: that arm reports every written argument and reads
    /// no position, so which of them is named cannot change what is scanned.
    /// </para>
    /// <para>
    /// The parameter shape is still compared, because reaching one arm is not on its own reaching it with
    /// the same arguments: a named argument binds by parameter name, and a position read out of one
    /// overload has to mean the same in the other. Parameter <em>types</em> are deliberately not compared,
    /// since no arm reads one — that is what lets the <see langword="string"/> and <see langword="bool"/>
    /// spellings of <c>.Bind</c> answer as one.
    /// </para>
    /// <para>
    /// A binding is the exception, and asks its arm's question instead of that proxy. #307 put a
    /// <c>CultureInfo</c> where the fourth argument used to be a setter, so the names no longer line up
    /// across the group; but <see cref="ReportBindArguments"/> reports the getter and everything after it
    /// without reading a role, so the only position it can disagree about is the getter's. Comparing
    /// names there would refuse a pair the arm reads identically, and the cost of that refusal is the one
    /// BCF3015 exists to prevent: no report at all where the author's own build is green and only this
    /// generator cannot resolve the type. A named argument still binds correctly, because every overload
    /// of <c>.Bind</c> names its getter <c>get</c>.
    /// </para>
    /// </remarks>
    private static bool AreInterchangeableOverloads(
        IMethodSymbol left, IMethodSymbol right, KnownSymbols symbols)
    {
        var kind = symbols.ClassifySurfaceMethod(left);
        if (kind != symbols.ClassifySurfaceMethod(right))
            return false;

        if (kind is SurfaceMethodKind.Bind or SurfaceMethodKind.ComponentBind)
        {
            return KnownSymbols.TryGetBindParameters(left, out var leftBind)
                && KnownSymbols.TryGetBindParameters(right, out var rightBind)
                && leftBind.GetterIndex == rightBind.GetterIndex;
        }

        var leftDeclared = left.ReducedFrom ?? left;
        var rightDeclared = right.ReducedFrom ?? right;

        if (leftDeclared.Parameters.Length != rightDeclared.Parameters.Length)
            return false;

        for (var index = 0; index < leftDeclared.Parameters.Length; index++)
        {
            if (leftDeclared.Parameters[index].Name != rightDeclared.Parameters[index].Name
                || leftDeclared.Parameters[index].IsParams != rightDeclared.Parameters[index].IsParams)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The design-time indexer <paramref name="elementAccess"/> resolves to, or <see langword="null"/> for any
    /// other indexer, an unrelated <c>_dict["k"]</c> must not be read as children.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="Recognize"/>'s failure recovery, for the same reason: this scanner runs
    /// on compilations where resolution failed, so the selected symbol is often absent. The last resort is
    /// the receiver's type, which still resolves when the access itself does not.
    /// </remarks>
    private static IPropertySymbol? TryGetRecognizedIndexer(
        ElementAccessExpressionSyntax elementAccess,
        ViewPartBodyContext context)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(elementAccess, context.CancellationToken);
        if (symbolInfo.Symbol is IPropertySymbol { IsIndexer: true } selected
            && IsRecognized(selected, context.KnownSymbols))
        {
            return selected;
        }

        foreach (var symbol in symbolInfo.CandidateSymbols)
        {
            if (symbol is IPropertySymbol { IsIndexer: true } candidate
                && IsRecognized(candidate, context.KnownSymbols))
            {
                return candidate;
            }
        }

        var receiverType = context.SemanticModel
            .GetTypeInfo(elementAccess.Expression, context.CancellationToken).Type;

        return FindIndexerByReceiverType(receiverType, context.KnownSymbols);
    }

    /// <summary>
    /// The known indexer declared by <paramref name="receiverType"/>, matched through the type rather than
    /// through the access's own symbol. Guarded on each known type being present:
    /// <c>SymbolEqualityComparer.Default.Equals(x, null)</c> answers <see langword="true"/> for a null
    /// <c>x</c>, so against a runtime without the bracket surface an unguarded comparison would match every
    /// indexer whose receiver type failed to resolve.
    /// </summary>
    private static IPropertySymbol? FindIndexerByReceiverType(
        ITypeSymbol? receiverType, KnownSymbols symbols)
    {
        if (receiverType is null)
            return null;

        var definition = receiverType.OriginalDefinition;

        if (symbols.ElementIndexer is { } elementIndexer
            && symbols.ElementViewType is { } elementViewType
            && SymbolEqualityComparer.Default.Equals(definition, elementViewType))
        {
            return elementIndexer;
        }

        if (symbols.ComponentIndexer is { } componentIndexer
            && symbols.ComponentViewType is { } componentViewType
            && SymbolEqualityComparer.Default.Equals(definition, componentViewType))
        {
            return componentIndexer;
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="indexer"/> is a child list this scanner has an arm for, which is two of the
    /// three <see cref="KnownSymbols.ClassifyChildrenIndexer"/> answers.
    /// </summary>
    /// <remarks>
    /// <c>SlotView</c>'s indexer is deliberately visible here as the answer this pattern does not admit,
    /// rather than as a comparison nobody wrote. It was never added when #176 introduced that indexer and no
    /// reason was recorded, so the omission is unexamined: this scanner therefore treats the brackets of a
    /// content-taking <c>[ViewPart]</c> call as not a surface child list, and an unresolved type written
    /// inside them stays the generic BCF1003 rather than BCF3015. Widening it is a change to what an Error
    /// reaches and wants its own measurement, so it is not made here.
    /// </remarks>
    private static bool IsRecognized(IPropertySymbol indexer, KnownSymbols symbols) =>
        symbols.ClassifyChildrenIndexer(indexer)
            is ChildrenIndexerKind.Element or ChildrenIndexerKind.Component;

    private static bool IsHtmlForEachInScope(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? known,
        ViewPartBodyContext context)
    {
        if (known is null
            || invocation.Expression is not SimpleNameSyntax name
            || name.Identifier.ValueText != known.Name)
            return false;

        foreach (var symbol in context.SemanticModel.LookupSymbols(
                     invocation.Expression.SpanStart,
                     name: known.Name,
                     includeReducedExtensionMethods: true))
        {
            if (symbol is IMethodSymbol candidate
                && SymbolEqualityComparer.Default.Equals(
                    KnownSymbols.Normalize(candidate),
                    KnownSymbols.Normalize(known)))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddRecognizedCandidate(
        ISymbol symbol,
        List<IMethodSymbol> candidates,
        ViewPartBodyContext context)
    {
        if (symbol is not IMethodSymbol method || !IsRecognized(method, context))
            return;

        foreach (var existing in candidates)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    KnownSymbols.Normalize(existing),
                    KnownSymbols.Normalize(method)))
                return;
        }

        candidates.Add(method);
    }

    /// <summary>
    /// Whether <paramref name="method"/> is something this scanner has an arm for: a method of the
    /// design-time surface, or a <c>[ViewPart]</c> call.
    /// </summary>
    /// <remarks>
    /// This is <see cref="KnownSymbols.IsDesignTimeApiMember"/> asked of a method, and calling it rather than
    /// restating its two disjuncts is deliberate: the set this scanner has an arm for and the set the design
    /// calls 設計時API are the same set, and they were written out separately until #68 gave the second one a
    /// name. The classification half is also the single lookup <see cref="ScanRenderExpression"/> dispatches
    /// on, so a method this answers <see langword="true"/> for is a method that switch has a case for, and
    /// neither can quietly stop agreeing with the other. Should the two sets ever need to diverge, that is a
    /// reason to write down here, not a second copy to maintain.
    /// </remarks>
    private static bool IsRecognized(IMethodSymbol method, ViewPartBodyContext context) =>
        context.KnownSymbols.IsDesignTimeApiMember(method);

    /// <summary>
    /// Whether <paramref name="kind"/> is one of the decorations written onto an element, the group
    /// <see cref="ScanDecoration"/> serves.
    /// </summary>
    private static bool IsElementDecoration(SurfaceMethodKind kind) =>
        kind is SurfaceMethodKind.Class
            or SurfaceMethodKind.AttributeShortcut
            or SurfaceMethodKind.EventShortcut
            or SurfaceMethodKind.Attr
            or SurfaceMethodKind.On
            or SurfaceMethodKind.Bind;

    private static bool IsFluentExtensionInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ViewPartBodyContext context)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
            return false;

        if (method.ReducedFrom is not null)
            return true;

        // Failure recovery can return the known unreduced symbol even for fluent syntax. Distinguish
        // that case from an unsupported static call by the receiver, not by ReducedFrom alone.
        return context.SemanticModel.GetSymbolInfo(access.Expression, context.CancellationToken).Symbol
                is not INamedTypeSymbol receiverType
            || !SymbolEqualityComparer.Default.Equals(receiverType, method.ContainingType);
    }

    private static ExpressionSyntax? Receiver(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax access ? access.Expression : null;

    private readonly struct BoundArguments
    {
        private readonly ImmutableArray<ExpressionSyntax?> _byDeclaredParameter;

        private BoundArguments(
            ImmutableArray<ExpressionSyntax?> byDeclaredParameter,
            ImmutableArray<ChildExpression> paramsElements,
            bool hasUnanalyzableParamsArgument)
        {
            _byDeclaredParameter = byDeclaredParameter;
            ParamsElements = paramsElements;
            HasUnanalyzableParamsArgument = hasUnanalyzableParamsArgument;
        }

        public ImmutableArray<ChildExpression> ParamsElements { get; }

        public bool HasUnanalyzableParamsArgument { get; }

        public IEnumerable<ExpressionSyntax> ExplicitArguments
        {
            get
            {
                foreach (var argument in _byDeclaredParameter)
                {
                    if (argument is not null)
                        yield return argument;
                }

                foreach (var argument in ParamsElements)
                    yield return argument.Expression;
            }
        }

        /// <summary>
        /// Whether a written argument landed on declared parameter <paramref name="index"/>. Answers what
        /// <see cref="At"/> answers without its walk back up to the <see cref="ArgumentSyntax"/>, for
        /// callers weighing a parameter list rather than reading an argument.
        /// </summary>
        public bool HasArgumentAt(int index) =>
            (uint)index < (uint)_byDeclaredParameter.Length && _byDeclaredParameter[index] is not null;

        public ArgumentSyntax? At(int index) =>
            (uint)index < (uint)_byDeclaredParameter.Length && _byDeclaredParameter[index] is { } expression
                ? expression.FirstAncestorOrSelf<ArgumentSyntax>()
                : null;

        public static BoundArguments FromFactory(FactoryArguments arguments, int declaredCount)
        {
            var byParameter = ImmutableArray.CreateBuilder<ExpressionSyntax?>(declaredCount);
            for (var index = 0; index < declaredCount; index++)
                byParameter.Add(arguments.At(index)?.Expression);

            return new BoundArguments(
                byParameter.MoveToImmutable(),
                arguments.ParamsElements,
                arguments.HasUnanalyzableParamsArgument);
        }

        public static BoundArguments? TryBindFallback(
            InvocationExpressionSyntax invocation,
            IMethodSymbol selectedMethod)
        {
            // Argument space is all this binds into, so the parameter list is taken in whatever spelling
            // arrived and the receiver skip asked of it: 0 for a reduced method, whose list already
            // excludes the receiver, 1 for an unreduced one (#211).
            return TryBindFallback(
                invocation.ArgumentList,
                selectedMethod.Parameters,
                KnownSymbols.ReceiverOffset(selectedMethod));
        }

        /// <summary>
        /// Binds an argument list to declared parameters without asking Roslyn for an operation, for the
        /// compilations this scanner exists for: where symbol resolution failed and
        /// <c>GetOperation</c> is unusable. A <see cref="BracketedArgumentListSyntax"/> binds through the
        /// same code as a parenthesized one, the C# positional/named rules do not differ between them.
        /// </summary>
        /// <remarks>
        /// A collection-expression literal passed whole (<c>Div[["a", "b"]]</c>) is unwrapped into its
        /// elements here, mirroring <see cref="FactoryArguments"/>: it is the same call as the expanded
        /// form, and leaving it whole made every name inside it invisible to the sweep, so such a body
        /// reported a bare BCF1003 and never named the value that could not be moved into generated code
        /// (#75). Reached when the element access itself has no operation, an unbound spread beside the
        /// children is the measured route, since that makes the whole element access an invalid operation
        /// and <see cref="FactoryArguments.Bind"/> returns before any element is examined.
        /// </remarks>
        public static BoundArguments? TryBindFallback(
            BaseArgumentListSyntax argumentList,
            ImmutableArray<IParameterSymbol> parameters,
            int offset)
        {
            var declaredCount = parameters.Length - offset;
            // Mutating this to <= 0 is a stryker survivor, measured equivalent rather than assumed, by
            // the same reasoning as the parallel check in HasValidArgumentOrder above. declaredCount ==
            // 0 additionally looks unreachable through this overload's own callers: the indexer caller's
            // offset is always 0 against a real indexer's parameter list, which is never empty, and the
            // invocation caller only runs once FactoryArguments.Bind has already failed for this call —
            // measured with the same throwaway probe (Div.Attr("x", MissingMethod() +
            // typeof(Probe).Name).PreventDefault()) that a zero-argument call's own operation binds
            // fine regardless of what is broken in its receiver chain, so the fallback this guards is
            // never reached with zero declared parameters to begin with.
            if (declaredCount < 0)
                return null;

            var byParameter = new ExpressionSyntax?[declaredCount];
            var paramsElements = ImmutableArray.CreateBuilder<ChildExpression>();
            var hasUnanalyzableParams = false;
            var nextPositional = 0;

            foreach (var argument in argumentList.Arguments)
            {
                int index;
                if (argument.NameColon is { } nameColon)
                {
                    index = UnresolvedValueTypeScanner.FindParameter(
                        parameters,
                        offset,
                        nameColon.Name.Identifier.ValueText);
                    if (index < 0)
                        return null;
                }
                else
                {
                    while (nextPositional < declaredCount && byParameter[nextPositional] is not null)
                        nextPositional++;

                    index = nextPositional;
                }

                if ((uint)index >= (uint)declaredCount)
                    return null;

                var parameter = parameters[index + offset];
                if (parameter.IsParams)
                {
                    if (argument.NameColon is not null)
                    {
                        if (hasUnanalyzableParams || paramsElements.Count != 0)
                            return null;

                        hasUnanalyzableParams = true;
                    }
                    else if (hasUnanalyzableParams)
                    {
                        return null;
                    }
                    else if (TryGetLiteralChildren(argumentList, argument) is { } literalChildren)
                    {
                        paramsElements.AddRange(literalChildren);
                    }
                    else
                    {
                        paramsElements.Add(new ChildExpression(argument.Expression, IsSpread: false));
                    }

                    nextPositional = index;
                    continue;
                }

                if (byParameter[index] is not null)
                    return null;

                byParameter[index] = argument.Expression;
                if (argument.NameColon is null)
                    nextPositional = index + 1;
            }

            return new BoundArguments(
                ImmutableArray.Create(byParameter),
                paramsElements.ToImmutable(),
                hasUnanalyzableParams);
        }

        /// <summary>
        /// The written children of <paramref name="argument"/> when it is a collection-expression literal
        /// passed whole to the <c>params</c> parameter, or <see langword="null"/> when it is anything else.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only a lone argument can be that literal: written beside other arguments it would be one element
        /// of the bucket, not the whole bucket.
        /// </para>
        /// <para>
        /// A spread's operand is collected and marked, exactly as <c>FactoryArguments</c> collects it, so
        /// that a spliced child list is scanned on this path too. An element this method cannot read at
        /// all is skipped rather than abandoning the whole literal, which is where this deliberately parts
        /// from <c>FactoryArguments</c>. There, a literal it cannot fully recover is refused outright,
        /// because emitting a partially recovered child list would drop children the author wrote. Nothing
        /// is emitted from here, because this binder feeds a diagnostic sweep, so the same caution would
        /// only silence BCF3015 on the children that <em>are</em> readable.
        /// </para>
        /// </remarks>
        private static List<ChildExpression>? TryGetLiteralChildren(
            BaseArgumentListSyntax argumentList, ArgumentSyntax argument)
        {
            if (argumentList.Arguments.Count != 1
                || argument.Expression is not CollectionExpressionSyntax literal)
            {
                return null;
            }

            var children = new List<ChildExpression>(literal.Elements.Count);

            foreach (var element in literal.Elements)
            {
                switch (element)
                {
                    case ExpressionElementSyntax expressionElement:
                        children.Add(new ChildExpression(expressionElement.Expression, IsSpread: false));
                        break;

                    case SpreadElementSyntax spread:
                        children.Add(new ChildExpression(spread.Expression, IsSpread: true));
                        break;
                }
            }

            return children;
        }
    }
}

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
    public static void Report(ExpressionSyntax root, ComposableBodyContext context) =>
        ScanRenderExpression(root, context);

    private static void ScanRenderExpression(ExpressionSyntax? expression, ComposableBodyContext context)
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

        // A recognized call this scanner could not read through — its overload group named no single
        // overload, or the arguments did not bind to the one it named. Neither failure says anything about
        // the receiver, which is the same expression either way, so the chain below is still walked. It is
        // only the arguments that go unread, because only the overload says what each of them means (#197).
        if (recognized.Method is not { } method
            || BindArguments(invocation, method, context) is not { } args)
        {
            ScanRenderExpression(Receiver(invocation), context);
            return;
        }

        var symbols = context.KnownSymbols;
        var recoverOwnValue = context.ShouldRecoverUnresolvedValue(invocation.Span);

        // Element(tag) carries no children on this surface: they are written in brackets on the
        // ElementBuilder it returns, and ScanChildrenIndexer handles that. The tag itself is never reported
        // on, whether or not it is constant.
        if (symbols.IsElementFactory(method))
            return;

        if (Is(method, symbols.HtmlIf))
        {
            ReportValue(args.At(0)?.Expression, context);
            ScanLambdaBody(args.At(1)?.Expression, context);
            ScanLambdaBody(args.At(2)?.Expression, context);
            return;
        }

        if (Is(method, symbols.HtmlForEach))
        {
            ReportValue(args.At(0)?.Expression, context);
            ReportLambdaValueBody(args.At(1)?.Expression, context);
            ScanLambdaBody(args.At(2)?.Expression, context);
            return;
        }

        // As with Element(tag): Component<T>() takes no arguments at all, and its children arrive on the
        // ComponentView<T> indexer, which ScanChildrenIndexer handles.
        if (UnresolvedComponentTypeScanner.IsComponentFactory(method, symbols.HtmlComponent))
            return;

        if (Is(method, symbols.HtmlRaw))
        {
            ReportValue(args.At(0)?.Expression, context);
            return;
        }

        if (Is(method, symbols.HtmlFragment))
        {
            ScanChildren(args, context);
            return;
        }

        // .Param, .Template and .Bind share a route, as they do in RenderExpressionAnalyzer.Classify: all
        // three chain off a ComponentView<T> receiver that has to be walked, and all three take the
        // selector in the same position. Only what follows the selector differs.
        var componentParameterKind = symbols.ClassifyComponentParameterMethod(method);
        var isComponentBind = Contains(symbols.ComponentBindMethods, method.OriginalDefinition);
        if (componentParameterKind != ComponentParameterMethodKind.None || isComponentBind)
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (!recoverOwnValue)
                return;

            if (isComponentBind)
            {
                // The getter and, where written, the setter: both are transplanted into generated code and
                // can therefore name a type that does not resolve. The selector is not a value position —
                // it is read for the property it names and never emitted, so a bad one is BCF3005's to
                // report. That is the same split the element-level .Bind arm below makes.
                ReportValue(args.At(1)?.Expression, context);
                ReportValue(args.At(2)?.Expression, context);
                return;
            }

            switch (componentParameterKind)
            {
                case ComponentParameterMethodKind.ScalarParam:
                    ReportValue(args.At(1)?.Expression, context);
                    break;
                case ComponentParameterMethodKind.GenericTemplateContextual:
                    ScanLambdaBody(args.At(1)?.Expression, context);
                    break;
                default:
                    ScanRenderExpression(args.At(1)?.Expression, context);
                    break;
            }

            return;
        }

        var normalized = KnownSymbols.Normalize(method);
        if (IsDecorationMethod(normalized, symbols)
            && !IsFluentExtensionInvocation(invocation, method, context))
        {
            return;
        }

        if (symbols.ClassMethod is not null
            && SymbolEqualityComparer.Default.Equals(normalized, KnownSymbols.Normalize(symbols.ClassMethod)))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (recoverOwnValue)
                ReportValue(args.At(0)?.Expression, context);
            return;
        }

        if (symbols.AttributeShortcuts.ContainsKey(normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (recoverOwnValue)
                ReportValue(args.At(0)?.Expression, context);
            return;
        }

        if (symbols.EventShortcuts.ContainsKey(normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (recoverOwnValue)
                ReportValue(args.At(0)?.Expression, context);
            return;
        }

        if (Contains(symbols.AttrMethods, normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (!recoverOwnValue)
                return;

            if (IsNonEmptyConstantString(args.At(0)?.Expression, context))
                ReportValue(args.At(1)?.Expression, context);
            else
                ReportSelectedInvocationValues(args.At(1)?.Expression, context);
            return;
        }

        if (Contains(symbols.OnMethods, normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (!recoverOwnValue)
                return;

            if (IsNonEmptyConstantString(args.At(0)?.Expression, context))
                ReportValue(args.At(1)?.Expression, context);
            else
                ReportSelectedInvocationValues(args.At(1)?.Expression, context);
            return;
        }

        if (Contains(symbols.BindMethods, normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (!recoverOwnValue)
                return;

            // The getter and the setter, both of which are transplanted into generated code and can
            // therefore name a type that does not resolve. The two name arguments are not value
            // positions: a non-constant one is BCF3011's to report, and that rejection has already
            // cleared recoverOwnValue by the time this runs.
            ReportValue(args.At(2)?.Expression, context);
            ReportValue(args.At(3)?.Expression, context);
            return;
        }

        if (IsComposable(method, context))
        {
            foreach (var argument in args.ExplicitArguments)
                ReportValue(argument, context);
        }
    }

    /// <summary>
    /// Scans an element access whose indexer is one of the design-time surface's. Both indexers,
    /// <c>ElementBuilder</c>'s and <c>ComponentView&lt;T&gt;</c>'s, take the same route: the receiver carries
    /// the tag, the decoration chain or the component parameter chain and is scanned as an expression in
    /// its own right, and the bracketed arguments are the children.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is gated on <see cref="ComposableBodyContext.ShouldRecoverUnresolvedValue"/>. That gate
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
        ElementAccessExpressionSyntax elementAccess, ComposableBodyContext context)
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
    /// <c>Element(tag)</c> call whose tag is not a non-empty constant string, the shape BCF3009 rejects.
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
        ExpressionSyntax? receiver, ComposableBodyContext context)
    {
        while (receiver is InvocationExpressionSyntax invocation)
        {
            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                is not IMethodSymbol method)
            {
                return false;
            }

            if (context.KnownSymbols.IsElementFactory(method))
            {
                // The tag has to be reached before it can be called non-constant. A binding failure is not
                // evidence of a rejected tag, and answering true on one would suppress the children's
                // diagnostics on nothing.
                return BindArguments(invocation, method, context)?.At(0)?.Expression is { } tag
                    && !IsNonEmptyConstantString(tag, context);
            }

            if (!IsDecorationMethod(KnownSymbols.Normalize(method), context.KnownSymbols))
                return false;

            receiver = Receiver(invocation);
        }

        return false;
    }

    private static bool IsTextOrRenderFragment(ExpressionSyntax expression, ComposableBodyContext context)
    {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        return type is { SpecialType: SpecialType.System_String }
            || (context.KnownSymbols.RenderFragmentType is { } renderFragment
                && SymbolEqualityComparer.Default.Equals(type, renderFragment));
    }

    private static void ScanChildren(BoundArguments args, ComposableBodyContext context)
    {
        if (args.HasUnanalyzableParamsArgument)
            return;

        foreach (var child in args.ParamsElements)
            ScanRenderExpression(child, context);
    }

    private static void ScanLambdaBody(ExpressionSyntax? expression, ComposableBodyContext context)
    {
        ScanRenderExpression(GetLambdaBody(expression), context);
    }

    private static void ReportLambdaValueBody(ExpressionSyntax? expression, ComposableBodyContext context) =>
        ReportValue(GetLambdaBody(expression), context);

    private static ExpressionSyntax? GetLambdaBody(ExpressionSyntax? expression) =>
        expression switch
        {
            SimpleLambdaExpressionSyntax { Body: ExpressionSyntax simpleBody } => simpleBody,
            ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax parenthesizedBody } => parenthesizedBody,
            _ => null,
        };

    private static void ReportValue(ExpressionSyntax? expression, ComposableBodyContext context)
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
    private static bool IsInsideUnselectedInvocation(SimpleNameSyntax name, ComposableBodyContext context) =>
        name.Ancestors().Any(ancestor => ancestor switch
        {
            // Asked of the call, not of its overload: a recognized call whose overload could not be named
            // is one the sweep walks through, so a value under it is reached and must not be suppressed
            // here on the way out.
            InvocationExpressionSyntax invocation =>
                context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is null
                    && !Recognize(invocation, context).IsSurfaceCall,
            ElementAccessExpressionSyntax elementAccess =>
                context.SemanticModel.GetSymbolInfo(elementAccess, context.CancellationToken).Symbol is null
                    && TryGetRecognizedIndexer(elementAccess, context) is null,
            _ => false,
        });

    // The caller did not select a decoration route, but a deliberately invoked user method in its value
    // still has its own source expression and must retain diagnostics (notably an escaped @nameof method).
    // Do not walk arbitrary error invocations here: those have no selected symbol and are not emitted.
    private static void ReportSelectedInvocationValues(ExpressionSyntax? expression, ComposableBodyContext context)
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
                        && !Recognize(invocation, context).IsSurfaceCall =>
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
    private static bool IsInsideNameofConstant(SimpleNameSyntax name, ComposableBodyContext context) =>
        name.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            ExpressionTemplateFactory.TryCreateNameofConstant(invocation, context) is not null
                && invocation.ArgumentList.Span.Contains(name.Span));

    private static bool IsNonEmptyConstantString(ExpressionSyntax? expression, ComposableBodyContext context) =>
        expression is not null
        && context.SemanticModel.GetConstantValue(expression, context.CancellationToken) is
        { HasValue: true, Value: string value }
        && !string.IsNullOrWhiteSpace(value);

    private static BoundArguments? BindArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ComposableBodyContext context)
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
        ComposableBodyContext context)
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
        ComposableBodyContext context)
    {
        var method = selectedMethod.ReducedFrom ?? selectedMethod;
        var offset = method.IsExtensionMethod
            && IsFluentExtensionInvocation(invocation, selectedMethod, context)
                ? 1
                : 0;

        return HasValidArgumentOrder(invocation.ArgumentList, method.Parameters, offset);
    }

    private static bool HasValidArgumentOrder(
        BaseArgumentListSyntax argumentList,
        ImmutableArray<IParameterSymbol> parameters,
        int offset)
    {
        var parameterCount = parameters.Length - offset;
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

    private static int WrittenParameterCount(IMethodSymbol method)
    {
        method = method.ReducedFrom ?? method;
        return method.Parameters.Length - (method.IsExtensionMethod ? 1 : 0);
    }

    private static RecognizedInvocation Recognize(
        InvocationExpressionSyntax invocation,
        ComposableBodyContext context)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method && IsRecognized(method, context))
            return RecognizedInvocation.Named(method);

        if (context.KnownSymbols.HtmlForEach is { } forEachInScope
            && IsHtmlForEachInScope(invocation, forEachInScope, context))
        {
            return RecognizedInvocation.Named(forEachInScope);
        }

        var candidates = new List<IMethodSymbol>();
        foreach (var symbol in symbolInfo.CandidateSymbols)
            AddRecognizedCandidate(symbol, candidates, context);

        var expressionInfo = context.SemanticModel.GetSymbolInfo(invocation.Expression, context.CancellationToken);
        if (expressionInfo.Symbol is IMethodSymbol expressionMethod)
            AddRecognizedCandidate(expressionMethod, candidates, context);

        foreach (var symbol in expressionInfo.CandidateSymbols)
            AddRecognizedCandidate(symbol, candidates, context);

        if (candidates.Count > 0)
            return RecognizedInvocation.FromGroup(TrySelectCandidate(invocation, candidates, context));

        if (invocation.Expression is not SimpleNameSyntax invocationName)
            return RecognizedInvocation.None;

        foreach (var symbol in context.SemanticModel.LookupSymbols(
                     invocation.SpanStart,
                     name: invocationName.Identifier.ValueText,
                     includeReducedExtensionMethods: true))
        {
            AddRecognizedCandidate(symbol, candidates, context);
        }

        if (TrySelectCandidate(invocation, candidates, context) is { } recovered)
            return RecognizedInvocation.Named(recovered);

        if (context.KnownSymbols.HtmlForEach is { } forEachByName
            && IsHtmlForEachInScope(invocation, forEachByName, context))
        {
            return RecognizedInvocation.Named(forEachByName);
        }

        return RecognizedInvocation.FromGroup(null, candidates.Count > 0);
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
        private RecognizedInvocation(IMethodSymbol? method, bool isSurfaceCall)
        {
            Method = method;
            IsSurfaceCall = isSurfaceCall;
        }

        /// <summary>Not a call this scanner knows: neither answer is available.</summary>
        public static RecognizedInvocation None => default;

        /// <summary>The overload this call selects, or <see langword="null"/> when none could be named.</summary>
        public IMethodSymbol? Method { get; }

        /// <summary>
        /// Whether the call names a surface method, whether or not one overload of it could be named.
        /// </summary>
        public bool IsSurfaceCall { get; }

        public static RecognizedInvocation Named(IMethodSymbol method) => new(method, isSurfaceCall: true);

        /// <summary>
        /// The answer for a non-empty group of recognized candidates, <paramref name="selected"/> being
        /// what <see cref="TrySelectCandidate"/> made of it. A group that named no overload is still a
        /// surface call.
        /// </summary>
        public static RecognizedInvocation FromGroup(IMethodSymbol? selected, bool isSurfaceCall = true) =>
            new(selected, isSurfaceCall);
    }

    /// <summary>
    /// The one candidate in <paramref name="candidates"/> that the call's written arguments select, or
    /// <see langword="null"/> when the group leaves the choice open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A single candidate is returned as it stands. Beyond that, an unresolved type inside a <c>.Bind</c>
    /// argument is what makes the invocation fail to bind, and failed overload resolution hands back the
    /// whole overload group — six for <c>Decorations.Bind</c>, three for <c>ComponentView&lt;T&gt;.Bind</c>
    /// — including the ones no argument count could have selected. Refusing the group outright is what left
    /// BCF3015 unreachable on both <c>.Bind</c> surfaces (#197).
    /// </para>
    /// <para>
    /// So the survivors are those the written arguments fill exactly, which keeps a longer overload from
    /// answering a shorter call. Filling is not on its own enough to name one: the element surface declares
    /// its getter-only <c>.Bind</c> twice, once per bindable type, and four written arguments fill four
    /// overloads. Those survivors are accepted anyway, because overloads of one method agree about what
    /// each argument position means and this scanner never reads a parameter's type. Survivors from two
    /// <em>different</em> surface methods would not agree, and refuse.
    /// </para>
    /// <para>
    /// Only the accepting half of that is covered by a test, and deliberately so rather than by oversight.
    /// Every overload group on this surface is prefix-aligned — <c>.Bind</c>'s fourth parameter is the
    /// setter on every overload that declares one — and every recognized name resolves to a single method,
    /// so no call site can currently be written whose reading changes with the survivor picked. Answering
    /// the first candidate that merely binds passes the whole suite. What this buys is that adding an
    /// overload that breaks either property costs a refused diagnostic rather than a silently misread one,
    /// which is the trade #197 asked for; a later reader finding it untested should weigh it on that, not
    /// take the absent test for a gap to fill.
    /// </para>
    /// </remarks>
    private static IMethodSymbol? TrySelectCandidate(
        InvocationExpressionSyntax invocation,
        List<IMethodSymbol> candidates,
        ComposableBodyContext context)
    {
        if (candidates.Count == 1)
            return candidates[0];

        IMethodSymbol? selected = null;

        foreach (var candidate in candidates)
        {
            if (BindArguments(invocation, candidate, context) is not { } args
                || !FillsEveryParameter(args, candidate))
            {
                continue;
            }

            if (selected is null)
            {
                selected = candidate;
                continue;
            }

            if (!AreInterchangeableOverloads(selected, candidate))
                return null;
        }

        return selected;
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
        var declared = method.ReducedFrom ?? method;
        var offset = declared.IsExtensionMethod ? 1 : 0;

        for (var index = 0; index < declared.Parameters.Length - offset; index++)
        {
            var parameter = declared.Parameters[index + offset];
            if (!parameter.IsParams && !parameter.IsOptional && args.At(index) is null)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="left"/> and <paramref name="right"/> are overloads of one method that bind
    /// arguments the same way, which is the whole of what this scanner reads of a candidate.
    /// </summary>
    /// <remarks>
    /// Sameness is asked of the declaration, not of the arm: two overloads of one surface method reach one
    /// arm by construction, whereas checking the arm alone would call <c>.Class</c> and an attribute
    /// shortcut interchangeable on nothing sturdier than their both reading argument 0. Parameter types are
    /// deliberately not compared, since no arm reads one — that is what lets the <see langword="string"/>
    /// and <see langword="bool"/> spellings of <c>.Bind</c> answer as one.
    /// </remarks>
    private static bool AreInterchangeableOverloads(IMethodSymbol left, IMethodSymbol right)
    {
        var leftDeclared = left.ReducedFrom ?? left;
        var rightDeclared = right.ReducedFrom ?? right;

        if (leftDeclared.Name != rightDeclared.Name
            || !SymbolEqualityComparer.Default.Equals(
                leftDeclared.ContainingType?.OriginalDefinition,
                rightDeclared.ContainingType?.OriginalDefinition)
            || leftDeclared.IsExtensionMethod != rightDeclared.IsExtensionMethod
            || leftDeclared.Parameters.Length != rightDeclared.Parameters.Length)
        {
            return false;
        }

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
        ComposableBodyContext context)
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
            && symbols.ElementBuilderType is { } elementBuilderType
            && SymbolEqualityComparer.Default.Equals(definition, elementBuilderType))
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

    private static bool IsRecognized(IPropertySymbol indexer, KnownSymbols symbols) =>
        (symbols.ElementIndexer is { } elementIndexer
            && SymbolEqualityComparer.Default.Equals(indexer.OriginalDefinition, elementIndexer))
        || (symbols.ComponentIndexer is { } componentIndexer
            && SymbolEqualityComparer.Default.Equals(indexer.OriginalDefinition, componentIndexer));

    private static bool IsHtmlForEachInScope(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? known,
        ComposableBodyContext context)
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
        ComposableBodyContext context)
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

    private static bool IsRecognized(IMethodSymbol method, ComposableBodyContext context)
    {
        var symbols = context.KnownSymbols;
        var normalized = KnownSymbols.Normalize(method);

        // No ElementTags lookup here, unlike the property route: every curated key is an IPropertySymbol and
        // `normalized` is keyed from an IMethodSymbol, so the lookup could only ever answer false.
        return symbols.IsElementFactory(method)
            || Is(method, symbols.HtmlIf)
            || Is(method, symbols.HtmlForEach)
            || UnresolvedComponentTypeScanner.IsComponentFactory(method, symbols.HtmlComponent)
            || Is(method, symbols.HtmlRaw)
            || Is(method, symbols.HtmlFragment)
            || symbols.ClassifyComponentParameterMethod(method) != ComponentParameterMethodKind.None
            || Contains(symbols.ComponentBindMethods, method.OriginalDefinition)
            || (symbols.ClassMethod is not null
                && SymbolEqualityComparer.Default.Equals(normalized, KnownSymbols.Normalize(symbols.ClassMethod)))
            || symbols.AttributeShortcuts.ContainsKey(normalized)
            || symbols.EventShortcuts.ContainsKey(normalized)
            || Contains(symbols.AttrMethods, normalized)
            || Contains(symbols.OnMethods, normalized)
            || Contains(symbols.BindMethods, normalized)
            || IsComposable(method, context);
    }

    private static bool IsComposable(IMethodSymbol method, ComposableBodyContext context)
    {
        var attribute = context.KnownSymbols.ComposableAttributeType;
        return attribute is not null && method.GetAttributes().Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));
    }

    private static bool Is(IMethodSymbol method, IMethodSymbol? known) =>
        known is not null && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, known);

    private static bool Contains(IReadOnlyCollection<ISymbol> symbols, ISymbol symbol)
    {
        foreach (var candidate in symbols)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, symbol))
                return true;
        }

        return false;
    }

    private static bool IsDecorationMethod(ISymbol method, KnownSymbols symbols) =>
        (symbols.ClassMethod is not null
            && SymbolEqualityComparer.Default.Equals(method, KnownSymbols.Normalize(symbols.ClassMethod)))
        || symbols.AttributeShortcuts.ContainsKey(method)
        || symbols.EventShortcuts.ContainsKey(method)
        || Contains(symbols.AttrMethods, method)
        || Contains(symbols.OnMethods, method)
        || Contains(symbols.BindMethods, method);

    private static bool IsFluentExtensionInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ComposableBodyContext context)
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
            ImmutableArray<ExpressionSyntax> paramsElements,
            bool hasUnanalyzableParamsArgument)
        {
            _byDeclaredParameter = byDeclaredParameter;
            ParamsElements = paramsElements;
            HasUnanalyzableParamsArgument = hasUnanalyzableParamsArgument;
        }

        public ImmutableArray<ExpressionSyntax> ParamsElements { get; }

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
                    yield return argument;
            }
        }

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
            var method = selectedMethod.ReducedFrom ?? selectedMethod;
            return TryBindFallback(
                invocation.ArgumentList, method.Parameters, method.IsExtensionMethod ? 1 : 0);
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
            if (declaredCount < 0)
                return null;

            var byParameter = new ExpressionSyntax?[declaredCount];
            var paramsElements = ImmutableArray.CreateBuilder<ExpressionSyntax>();
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
                        paramsElements.Add(argument.Expression);
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
        /// Spread elements are skipped rather than abandoning the whole literal, which is where this
        /// deliberately parts from <c>FactoryArguments</c>. There, a literal containing a spread is refused
        /// outright, because emitting a partially recovered child list would drop children the author wrote.
        /// Nothing is emitted from here, because this binder feeds a diagnostic sweep, so the same caution would
        /// only silence BCF3015 on the children that <em>are</em> written out. A spread's own operand is not
        /// collected either: it would never have been emitted as a child, and this scanner reports only on
        /// expressions the analyzer would emit.
        /// </para>
        /// </remarks>
        private static List<ExpressionSyntax>? TryGetLiteralChildren(
            BaseArgumentListSyntax argumentList, ArgumentSyntax argument)
        {
            if (argumentList.Arguments.Count != 1
                || argument.Expression is not CollectionExpressionSyntax literal)
            {
                return null;
            }

            var children = new List<ExpressionSyntax>(literal.Elements.Count);

            foreach (var element in literal.Elements)
            {
                if (element is ExpressionElementSyntax expressionElement)
                    children.Add(expressionElement.Expression);
            }

            return children;
        }
    }
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCompose.Compiler.Analysis;

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

        if (expression is not InvocationExpressionSyntax invocation
            || TryGetRecognizedMethod(invocation, context) is not { } method)
        {
            return;
        }

        if (BindArguments(invocation, method, context) is not { } args)
            return;

        var symbols = context.KnownSymbols;
        var recoverOwnValue = context.ShouldRecoverUnresolvedValue(invocation.Span);

        // Element(tag) carries no children on this surface — they are written in brackets on the
        // ElementBuilder it returns, and ScanChildrenIndexer handles that — and the tag itself is never
        // reported on, whether or not it is constant.
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

        if (Is(method, symbols.ParamMethod))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (recoverOwnValue)
                ReportValue(args.At(1)?.Expression, context);
            return;
        }

        if (Is(method, symbols.FragmentParamMethod))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (recoverOwnValue)
                ScanRenderExpression(args.At(1)?.Expression, context);
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

        if (IsComposable(method, context))
        {
            foreach (var argument in args.ExplicitArguments)
                ReportValue(argument, context);
        }
    }

    /// <summary>
    /// Scans an element access whose indexer is one of the design-time surface's.  Both indexers —
    /// <c>ElementBuilder</c>'s and <c>ComponentView&lt;T&gt;</c>'s — take the same route: the receiver carries
    /// the tag, the decoration chain or the <c>.Param</c> chain and is scanned as an expression in its own
    /// right, and the bracketed arguments are the children.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is gated on <see cref="ComposableBodyContext.ShouldRecoverUnresolvedValue"/>.  That gate
    /// exists so an arm does not re-report a value it already diagnosed, and this route has no value of its
    /// own: the tag belongs to the receiver, which carries its own gate.  Gating the children on it would
    /// silence expressions the rejection was never about.
    /// </para>
    /// <para>
    /// The children are scanned only when the receiver's tag survives BC3009.  A non-constant tag has
    /// already rejected the whole element, so nothing inside the brackets reaches generated code and a
    /// report about it would be noise on top of the real error.  The rule is not restated here: the gate
    /// asks the receiver, through <see cref="HasRejectedElementTag"/>, because the receiver is what owns
    /// the tag and what BC3009 is reported on.
    /// </para>
    /// </remarks>
    private static void ScanChildrenIndexer(
        ElementAccessExpressionSyntax elementAccess, ComposableBodyContext context)
    {
        if (TryGetRecognizedIndexer(elementAccess, context) is not { } indexer)
            return;

        ScanRenderExpression(elementAccess.Expression, context);

        // A non-constant tag has already been rejected by BC3009 on the receiver, so the children never
        // reach generated code and reporting on them is noise. This is the gate the method-surface Element
        // arm carried before #87 deleted it.
        if (HasRejectedElementTag(elementAccess.Expression, context))
            return;

        if (BindIndexerArguments(elementAccess, indexer, context) is { } args)
            ScanChildren(args, context);
    }

    /// <summary>
    /// Whether <paramref name="receiver"/> resolves through its decoration chain to an
    /// <c>Element(tag)</c> call whose tag is not a non-empty constant string — the shape BC3009 rejects.
    /// </summary>
    /// <remarks>
    /// The chain is unwound rather than inspected at the top, because decorations sit between the factory
    /// and the brackets: in <c>Element(t).Class("c")["x"]</c> the indexer's receiver is the <c>Class</c>
    /// invocation. A curated tag is a property reference and never reaches the loop's body, so it returns
    /// false and its children are scanned as before.
    /// <para>
    /// Every path fails open — an unresolved symbol, an unrecognized method, a missing receiver and a tag
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
        if (args.HasExplicitParamsArgument)
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
            InvocationExpressionSyntax invocation =>
                context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is null
                    && TryGetRecognizedMethod(invocation, context) is null,
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
                        && TryGetRecognizedMethod(invocation, context) is null =>
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
    /// Binds an indexer's bracketed arguments.  The fallback binder matters more here than anywhere else:
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

    private static IMethodSymbol? TryGetRecognizedMethod(
        InvocationExpressionSyntax invocation,
        ComposableBodyContext context)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is IMethodSymbol method && IsRecognized(method, context))
            return method;

        if (IsHtmlForEachInScope(invocation, context.KnownSymbols.HtmlForEach, context))
            return context.KnownSymbols.HtmlForEach;

        var candidates = new List<IMethodSymbol>();
        foreach (var symbol in symbolInfo.CandidateSymbols)
            AddRecognizedCandidate(symbol, candidates, context);

        var expressionInfo = context.SemanticModel.GetSymbolInfo(invocation.Expression, context.CancellationToken);
        if (expressionInfo.Symbol is IMethodSymbol expressionMethod)
            AddRecognizedCandidate(expressionMethod, candidates, context);

        foreach (var symbol in expressionInfo.CandidateSymbols)
            AddRecognizedCandidate(symbol, candidates, context);

        if (candidates.Count == 1)
            return candidates[0];

        if (candidates.Count > 1
            || invocation.Expression is not SimpleNameSyntax invocationName)
        {
            return null;
        }

        foreach (var symbol in context.SemanticModel.LookupSymbols(
                     invocation.SpanStart,
                     name: invocationName.Identifier.ValueText,
                     includeReducedExtensionMethods: true))
        {
            AddRecognizedCandidate(symbol, candidates, context);
        }

        if (candidates.Count == 1)
            return candidates[0];

        return IsHtmlForEachInScope(invocation, context.KnownSymbols.HtmlForEach, context)
            ? context.KnownSymbols.HtmlForEach
            : null;
    }

    /// <summary>
    /// The design-time indexer <paramref name="elementAccess"/> resolves to, or <see langword="null"/> for any
    /// other indexer — an unrelated <c>_dict["k"]</c> must not be read as children.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="TryGetRecognizedMethod"/>'s failure recovery, for the same reason: this scanner runs
    /// on compilations where resolution failed, so the selected symbol is often absent.  The last resort is
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
    /// through the access's own symbol.  Guarded on each known type being present:
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
            || Is(method, symbols.ParamMethod)
            || Is(method, symbols.FragmentParamMethod)
            || (symbols.ClassMethod is not null
                && SymbolEqualityComparer.Default.Equals(normalized, KnownSymbols.Normalize(symbols.ClassMethod)))
            || symbols.AttributeShortcuts.ContainsKey(normalized)
            || symbols.EventShortcuts.ContainsKey(normalized)
            || Contains(symbols.AttrMethods, normalized)
            || Contains(symbols.OnMethods, normalized)
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
        || Contains(symbols.OnMethods, method);

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
            bool hasExplicitParamsArgument)
        {
            _byDeclaredParameter = byDeclaredParameter;
            ParamsElements = paramsElements;
            HasExplicitParamsArgument = hasExplicitParamsArgument;
        }

        public ImmutableArray<ExpressionSyntax> ParamsElements { get; }

        public bool HasExplicitParamsArgument { get; }

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
                arguments.HasExplicitParamsArgument);
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
        /// <c>GetOperation</c> is unusable.  A <see cref="BracketedArgumentListSyntax"/> binds through the
        /// same code as a parenthesized one — the C# positional/named rules do not differ between them.
        /// </summary>
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
            var hasExplicitParams = false;
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
                        if (hasExplicitParams || paramsElements.Count != 0)
                            return null;

                        hasExplicitParams = true;
                    }
                    else if (hasExplicitParams)
                    {
                        return null;
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
                hasExplicitParams);
        }
    }
}

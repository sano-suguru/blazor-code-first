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

        if (expression is not InvocationExpressionSyntax invocation
            || TryGetRecognizedMethod(invocation, context) is not { } method)
        {
            return;
        }

        if (BindArguments(invocation, method, context) is not { } args)
            return;

        var symbols = context.KnownSymbols;
        if (symbols.ElementTags.ContainsKey(KnownSymbols.Normalize(method)))
        {
            ScanChildren(args, context);
            return;
        }

        if (Is(method, symbols.HtmlElement))
        {
            if (IsNonEmptyConstantString(args.At(0)?.Expression, context))
                ScanChildren(args, context);
            return;
        }

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

        if (UnresolvedComponentTypeScanner.IsComponentFactory(
                method,
                symbols.HtmlComponent,
                symbols.HtmlComponentWithChildren))
        {
            if (Is(method, symbols.HtmlComponentWithChildren))
                ScanChildren(args, context);
            return;
        }

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
            ReportValue(args.At(1)?.Expression, context);
            return;
        }

        if (Is(method, symbols.FragmentParamMethod))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ScanRenderExpression(args.At(1)?.Expression, context);
            return;
        }

        var normalized = KnownSymbols.Normalize(method);
        if (symbols.ClassMethod is not null
            && SymbolEqualityComparer.Default.Equals(normalized, KnownSymbols.Normalize(symbols.ClassMethod)))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(args.At(0)?.Expression, context);
            return;
        }

        if (symbols.AttributeShortcuts.ContainsKey(normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(args.At(0)?.Expression, context);
            return;
        }

        if (symbols.EventShortcuts.ContainsKey(normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(args.At(0)?.Expression, context);
            return;
        }

        if (Contains(symbols.AttrMethods, normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            if (IsNonEmptyConstantString(args.At(0)?.Expression, context))
                ReportValue(args.At(1)?.Expression, context);
            else
                ReportSelectedInvocationValues(args.At(1)?.Expression, context);
            return;
        }

        if (Contains(symbols.OnMethods, normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
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

    private static bool IsInsideUnselectedInvocation(SimpleNameSyntax name, ComposableBodyContext context) =>
        name.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is null
                && TryGetRecognizedMethod(invocation, context) is null);

    // The caller did not select a decoration route, but a deliberately invoked user method in its value
    // still has its own source expression and must retain diagnostics (notably an escaped @nameof method).
    // Do not walk arbitrary error invocations here: those have no selected symbol and are not emitted.
    private static void ReportSelectedInvocationValues(ExpressionSyntax? expression, ComposableBodyContext context)
    {
        if (expression is null)
            return;

        foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol
                && TryGetRecognizedMethod(invocation, context) is null)
            {
                foreach (var argument in invocation.ArgumentList.Arguments)
                    ReportValue(argument.Expression, context);
            }
        }
    }

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
        if (FactoryArguments.Bind(invocation, context) is { } factoryArguments)
            return BoundArguments.FromFactory(factoryArguments, method);

        return BoundArguments.TryBindFallback(invocation, method);
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
        return symbols.ElementTags.ContainsKey(normalized)
            || Is(method, symbols.HtmlElement)
            || Is(method, symbols.HtmlIf)
            || Is(method, symbols.HtmlForEach)
            || UnresolvedComponentTypeScanner.IsComponentFactory(
                method,
                symbols.HtmlComponent,
                symbols.HtmlComponentWithChildren)
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

        public static BoundArguments FromFactory(FactoryArguments arguments, IMethodSymbol method)
        {
            var declaredCount = WrittenParameterCount(method);
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
            var offset = method.IsExtensionMethod ? 1 : 0;
            var declaredCount = method.Parameters.Length - offset;
            if (declaredCount < 0)
                return null;

            var byParameter = new ExpressionSyntax?[declaredCount];
            var paramsElements = ImmutableArray.CreateBuilder<ExpressionSyntax>();
            var hasExplicitParams = false;
            var nextPositional = 0;

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                int index;
                if (argument.NameColon is { } nameColon)
                {
                    index = FindParameter(method, offset, nameColon.Name.Identifier.ValueText);
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

                var parameter = method.Parameters[index + offset];
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

        private static int WrittenParameterCount(IMethodSymbol method)
        {
            method = method.ReducedFrom ?? method;
            return method.Parameters.Length - (method.IsExtensionMethod ? 1 : 0);
        }

        private static int FindParameter(IMethodSymbol method, int offset, string name)
        {
            for (var ordinal = offset; ordinal < method.Parameters.Length; ordinal++)
            {
                if (method.Parameters[ordinal].Name == name)
                    return ordinal - offset;
            }

            return -1;
        }
    }
}

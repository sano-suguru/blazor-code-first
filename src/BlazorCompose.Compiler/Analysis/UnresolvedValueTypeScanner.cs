using System.Collections.Generic;
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

        var symbols = context.KnownSymbols;
        if (symbols.ElementTags.ContainsKey(KnownSymbols.Normalize(method)))
        {
            ScanChildren(invocation.ArgumentList.Arguments, 0, context);
            return;
        }

        if (Is(method, symbols.HtmlElement))
        {
            ScanChildren(invocation.ArgumentList.Arguments, 1, context);
            return;
        }

        if (Is(method, symbols.HtmlIf))
        {
            ReportValue(ArgumentAt(invocation, 0), context);
            ScanLambdaBody(ArgumentAt(invocation, 1), context);
            ScanLambdaBody(ArgumentAt(invocation, 2), context);
            return;
        }

        if (Is(method, symbols.HtmlForEach))
        {
            ReportValue(ArgumentAt(invocation, 0), context);
            ReportLambdaValueBody(ArgumentAt(invocation, 1), context);
            ScanLambdaBody(ArgumentAt(invocation, 2), context);
            return;
        }

        if (UnresolvedComponentTypeScanner.IsComponentFactory(
                method,
                symbols.HtmlComponent,
                symbols.HtmlComponentWithChildren))
        {
            if (Is(method, symbols.HtmlComponentWithChildren))
                ScanChildren(invocation.ArgumentList.Arguments, 0, context);
            return;
        }

        if (Is(method, symbols.HtmlRaw))
        {
            ReportValue(ArgumentAt(invocation, 0), context);
            return;
        }

        if (Is(method, symbols.HtmlFragment))
        {
            ScanChildren(invocation.ArgumentList.Arguments, 0, context);
            return;
        }

        if (Is(method, symbols.ParamMethod))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(ArgumentAt(invocation, 1), context);
            return;
        }

        if (Is(method, symbols.FragmentParamMethod))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ScanRenderExpression(ArgumentAt(invocation, 1), context);
            return;
        }

        var normalized = KnownSymbols.Normalize(method);
        if (symbols.ClassMethod is not null
            && SymbolEqualityComparer.Default.Equals(normalized, KnownSymbols.Normalize(symbols.ClassMethod)))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(ArgumentAt(invocation, 0), context);
            return;
        }

        if (symbols.AttributeShortcuts.ContainsKey(normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(ArgumentAt(invocation, 0), context);
            return;
        }

        if (symbols.EventShortcuts.ContainsKey(normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(ArgumentAt(invocation, 0), context);
            return;
        }

        if (Contains(symbols.AttrMethods, normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(ArgumentAt(invocation, 1), context);
            return;
        }

        if (Contains(symbols.OnMethods, normalized))
        {
            ScanRenderExpression(Receiver(invocation), context);
            ReportValue(ArgumentAt(invocation, 1), context);
            return;
        }

        if (IsComposable(method, context))
        {
            foreach (var argument in invocation.ArgumentList.Arguments)
                ReportValue(argument.Expression, context);
        }
    }

    private static bool IsTextOrRenderFragment(ExpressionSyntax expression, ComposableBodyContext context)
    {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        return type is { SpecialType: SpecialType.System_String }
            || (context.KnownSymbols.RenderFragmentType is { } renderFragment
                && SymbolEqualityComparer.Default.Equals(type, renderFragment));
    }

    private static void ScanChildren(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        int start,
        ComposableBodyContext context)
    {
        for (var index = start; index < arguments.Count; index++)
            ScanRenderExpression(arguments[index].Expression, context);
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
        if (expression is null || HasUnselectedInvocation(expression, context))
            return;

        foreach (var name in expression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (IsInsideNameofConstant(name, context))
                continue;

            _ = ExpressionTemplateFactory.TryReportUnresolvedType(name, context);
        }
    }

    private static bool HasUnselectedInvocation(ExpressionSyntax expression, ComposableBodyContext context) =>
        expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(invocation =>
            context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is null);

    private static bool IsInsideNameofConstant(SimpleNameSyntax name, ComposableBodyContext context) =>
        name.Ancestors().OfType<InvocationExpressionSyntax>().Any(invocation =>
            ExpressionTemplateFactory.TryCreateNameofConstant(invocation, context) is not null
                && invocation.ArgumentList.Span.Contains(name.Span));

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

    private static string? InvokedName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            SimpleNameSyntax name => name.Identifier.ValueText,
            MemberAccessExpressionSyntax { Name: SimpleNameSyntax name } => name.Identifier.ValueText,
            _ => null,
        };

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

    private static ExpressionSyntax? ArgumentAt(InvocationExpressionSyntax invocation, int index) =>
        (uint)index < (uint)invocation.ArgumentList.Arguments.Count
            ? invocation.ArgumentList.Arguments[index].Expression
            : null;
}

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Classifies a composable definition body expression into the statically sequenceable
/// <see cref="RenderTemplateNode"/> hierarchy.  Dynamic argument text is normalized through
/// <see cref="ExpressionTemplateFactory"/> so parameter references become holes and imports/containing
/// type context are preserved.  Nested <c>[Composable]</c> calls become <see cref="ComposableCallTemplateNode"/>.
/// Returns <see langword="null"/> when the expression cannot be statically analyzed.
/// </summary>
internal static class RenderExpressionAnalyzer
{
    public static RenderTemplateNode? Analyze(ExpressionSyntax expression, ComposableBodyContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        // Mixed content: a string-typed expression in any position (child, If branch, ForEach content)
        // becomes a bare text node. The pre-conversion Type is String even though it converts to View.
        if (context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type
                is { SpecialType: SpecialType.System_String })
        {
            return new TextContentTemplateNode(ExpressionTemplateFactory.Create(expression, context));
        }

        if (expression is not InvocationExpressionSyntax invocation)
            return null;

        if (context.SemanticModel
            .GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return null;

        var symbols = context.KnownSymbols;

        static bool Is(IMethodSymbol method, IMethodSymbol? known) =>
            known is not null && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, known);

        {
            string? tag = symbols.ElementTags.TryGetValue(KnownSymbols.Normalize(method), out var mapped)
                ? mapped
                : null;

            if (tag is null && Is(method, symbols.HtmlElement))
            {
                var tagArg = invocation.ArgumentList.Arguments[0].Expression;
                var constant = context.SemanticModel.GetConstantValue(tagArg, context.CancellationToken);
                if (constant is { HasValue: true, Value: string tagValue } &&
                    !string.IsNullOrWhiteSpace(tagValue))
                {
                    tag = tagValue;
                }
                else
                {
                    context.Diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC3009, tagArg.GetLocation(), []));
                    return null;
                }
            }

            if (tag is not null)
            {
                // Div/Span/Button: children are all args. Element: children are args[1..].
                bool isElement = Is(method, symbols.HtmlElement);
                var args = invocation.ArgumentList.Arguments;
                var children = ImmutableArray.CreateBuilder<RenderTemplateNode>();
                for (int i = isElement ? 1 : 0; i < args.Count; i++)
                {
                    var child = Analyze(args[i].Expression, context);
                    if (child is null)
                        return null;
                    children.Add(child);
                }

                return new ElementTemplateNode(tag, default, default, default, children.ToImmutable());
            }
        }

        if (Is(method, symbols.HtmlIf))
        {
            var condition = invocation.ArgumentList.Arguments[0].Expression;
            var thenExpr = ExtractLambdaBody(invocation.ArgumentList.Arguments[1].Expression);
            if (thenExpr is null)
                return null;

            var thenNode = Analyze(thenExpr, context);
            if (thenNode is null)
                return null;

            RenderTemplateNode? otherwiseNode = null;
            if (invocation.ArgumentList.Arguments.Count >= 3)
            {
                var otherwiseArg = invocation.ArgumentList.Arguments[2].Expression;
                if (otherwiseArg is not LiteralExpressionSyntax
                    { Token.RawKind: (int)SyntaxKind.NullKeyword })
                {
                    var otherwiseExpr = ExtractLambdaBody(otherwiseArg);
                    if (otherwiseExpr is null)
                        return null;

                    otherwiseNode = Analyze(otherwiseExpr, context);
                    if (otherwiseNode is null)
                        return null;
                }
            }

            return new IfTemplateNode(
                ExpressionTemplateFactory.Create(condition, context),
                thenNode,
                otherwiseNode);
        }

        if (Is(method, symbols.HtmlForEach))
        {
            var sourceExpression = invocation.ArgumentList.Arguments[0].Expression;
            if (!TryExtractSingleParameterLambda(
                    invocation.ArgumentList.Arguments[1].Expression, out var keyParameter, out var keyBody)
                || !TryExtractSingleParameterLambda(
                    invocation.ArgumentList.Arguments[2].Expression, out var contentParameter, out var contentBody))
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC3004,
                    invocation.GetLocation(),
                    []));
                return null;
            }

            if (context.SemanticModel.GetDeclaredSymbol(keyParameter, context.CancellationToken) is not { } keyParamSymbol
                || context.SemanticModel.GetDeclaredSymbol(contentParameter, context.CancellationToken) is not { } contentParamSymbol)
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC3004,
                    invocation.GetLocation(),
                    []));
                return null;
            }

            // Source references the enclosing scope (fields, composable params, outer items) — never this
            // item — so it is normalized before the iteration variable is registered.
            var source = ExpressionTemplateFactory.Create(sourceExpression, context);

            var itemOrdinal = context.PushIterationVariable(contentParamSymbol, keyParamSymbol);
            try
            {
                var key = ExpressionTemplateFactory.Create(keyBody, context);
                var content = Analyze(contentBody, context);
                if (content is null)
                    return null;

                if (!KeyReferencesItemOrdinal(key, itemOrdinal))
                {
                    context.Diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC3002,
                        invocation.ArgumentList.Arguments[1].GetLocation(),
                        []));
                }

                return new ForEachTemplateNode(
                    source,
                    key,
                    content,
                    TemplateLocation.From(invocation.GetLocation()));
            }
            finally
            {
                context.PopIterationVariable(contentParamSymbol, keyParamSymbol);
            }
        }

        if (Is(method, symbols.HtmlComponent))
        {
            // Base case: Html.Component<T>() with no .Param yet.
            var typeName = method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new ComponentTemplateNode(typeName, EquatableArray<ComponentParameter>.Empty);
        }

        if (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, symbols.ParamMethod))
        {
            // Chained: <ComponentView<T> receiver>.Param(selector, value). Recurse into the receiver to
            // reach the base Component<T>() (or an inner .Param), then append this parameter in source order.
            if (invocation.Expression is not MemberAccessExpressionSyntax paramAccess
                || Analyze(paramAccess.Expression, context) is not ComponentTemplateNode inner)
            {
                return null;
            }

            var selector = invocation.ArgumentList.Arguments[0].Expression;
            var valueExpression = invocation.ArgumentList.Arguments[1].Expression;

            if (!TryGetSelectorProperty(selector, context, out var property))
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC3005, selector.GetLocation(), []));
                return null;
            }

            if (!IsSettableParameter(property, context))
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC3006, selector.GetLocation(), [property.Name]));
                return null;
            }

            foreach (var existing in inner.Parameters.AsImmutableArray())
            {
                if (string.Equals(existing.Name, property.Name, System.StringComparison.Ordinal))
                {
                    context.Diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC3007, selector.GetLocation(), [property.Name]));
                    return null;
                }
            }

            var value = ExpressionTemplateFactory.Create(valueExpression, context);
            var appended = inner.Parameters.AsImmutableArray().Add(new ComponentParameter(property.Name, value));
            return new ComponentTemplateNode(inner.TypeName, appended);
        }

        bool isClass = symbols.ClassMethod is not null
            && SymbolEqualityComparer.Default.Equals(
                (method.ReducedFrom ?? method).OriginalDefinition, symbols.ClassMethod);
        bool isOnClick = symbols.OnClickMethod is not null
            && SymbolEqualityComparer.Default.Equals(
                (method.ReducedFrom ?? method).OriginalDefinition, symbols.OnClickMethod);

        if (isClass || isOnClick)
        {
            // Receiver of `<expr>.Class(arg)`/`<expr>.OnClick(arg)` is the member-access expression's receiver.
            if (invocation.Expression is not MemberAccessExpressionSyntax decoAccess)
                return null;

            var inner = Analyze(decoAccess.Expression, context);
            // null: unanalyzable or already diagnosed (including an inner decoration that reported BC3008) —
            // propagate silently so the decoration is not double-reported.
            if (inner is null)
                return null;

            var arg = invocation.ArgumentList.Arguments[0].Expression;

            switch (inner)
            {
                case ElementTemplateNode e when isClass:
                    return e with { Classes = e.Classes.AsImmutableArray().Add(ExpressionTemplateFactory.Create(arg, context)) };
                case ElementTemplateNode e when isOnClick:
                    return e with
                    {
                        Events = e.Events.AsImmutableArray().Add(new EventTemplate(
                            "onclick",
                            ExpressionTemplateFactory.Create(arg, context))),
                    };

                default:
                    // ElementTemplateNode is the only node with a single element frame a class/event can
                    // attach to. Every other node — If / ForEach / composable call / component / bare text
                    // content — has no such frame, so decorating it is always rejected.
                    context.Diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC3008, decoAccess.Name.GetLocation(), []));
                    return null;
            }
        }

        if (IsComposable(method, context))
        {
            var arguments = CreateInvocationArguments(invocation, method, context);
            if (arguments is null)
                return null;

            return new ComposableCallTemplateNode(
                MethodKey.Create(method),
                method.Name,
                arguments.Value,
                TemplateLocation.From(invocation.GetLocation()));
        }

        return null;
    }

    private static bool IsComposable(IMethodSymbol method, ComposableBodyContext context)
    {
        var attributeType = context.KnownSymbols.ComposableAttributeType;
        if (attributeType is null)
            return false;

        foreach (var attribute in method.OriginalDefinition.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                return true;
        }

        return false;
    }

    private static ImmutableArray<ComposableInvocationArgument>? CreateInvocationArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ComposableBodyContext context)
    {
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken)
            is not IInvocationOperation operation)
        {
            return null;
        }

        // Explicitly supplied arguments sort by their source position; implicit/default arguments sort
        // after every supplied argument.  Operation arguments are parameter-ordered, so the enumeration
        // index cannot be used as source order.
        var builder = ImmutableArray.CreateBuilder<ComposableInvocationArgument>(operation.Arguments.Length);
        foreach (var argument in operation.Arguments)
        {
            var parameter = argument.Parameter;
            if (parameter is null)
                return null;

            var isImplicitDefault = argument.ArgumentKind == ArgumentKind.DefaultValue;
            var parameterTypeName = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            ExpressionTemplate value;
            int sourceOrder;
            if (isImplicitDefault)
            {
                value = ConstantTemplate.ForParameterDefault(parameter, parameterTypeName);

                // Strictly increasing in parameter ordinal and always greater than any source
                // position (a small non-negative span start), so implicit defaults sort after every
                // supplied argument while staying in parameter order.  Subtracting the parameter count
                // before adding the ordinal keeps the value below int.MaxValue and cannot overflow,
                // unlike a formula that could add to int.MaxValue when trailing optionals are omitted.
                sourceOrder = int.MaxValue - method.Parameters.Length + parameter.Ordinal;
            }
            else
            {
                var argumentExpression = (argument.Syntax as ArgumentSyntax)?.Expression;
                if (argumentExpression is null)
                    return null;

                value = ExpressionTemplateFactory.Create(argumentExpression, context);
                sourceOrder = argument.Syntax.SpanStart;
            }

            builder.Add(new ComposableInvocationArgument(
                parameter.Ordinal,
                sourceOrder,
                parameterTypeName,
                isImplicitDefault,
                value));
        }

        return builder.ToImmutable();
    }

    private static ExpressionSyntax? ExtractLambdaBody(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax body } => body,
        SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } => body,
        _ => null,
    };

    private static bool TryExtractSingleParameterLambda(
        ExpressionSyntax expression,
        out ParameterSyntax parameter,
        out ExpressionSyntax body)
    {
        switch (expression)
        {
            case SimpleLambdaExpressionSyntax { Body: ExpressionSyntax simpleBody } simple:
                parameter = simple.Parameter;
                body = simpleBody;
                return true;
            // A list pattern ([var single]) on a SeparatedSyntaxList requires System.Index.GetOffset,
            // which is unavailable on netstandard2.0 (CS0656); match the single-parameter shape with an
            // explicit count check instead.
            case ParenthesizedLambdaExpressionSyntax { Body: ExpressionSyntax parenBody } paren
                when paren.ParameterList.Parameters.Count == 1:
                parameter = paren.ParameterList.Parameters[0];
                body = parenBody;
                return true;
            default:
                parameter = null!;
                body = null!;
                return false;
        }
    }

    private static bool KeyReferencesItemOrdinal(ExpressionTemplate key, int itemOrdinal)
    {
        foreach (var segment in key.Segments)
        {
            if (segment is ParameterHoleExpressionSegment hole && hole.ParameterOrdinal == itemOrdinal)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Succeeds only when <paramref name="selector"/> is <c>p =&gt; p.Property</c> — a member access whose
    /// receiver is the lambda's own single parameter. Rejects casts, method calls, null-conditional access,
    /// and members of a captured variable (whose receiver binds to something other than the parameter).
    /// </summary>
    private static bool TryGetSelectorProperty(
        ExpressionSyntax selector, ComposableBodyContext context, [MaybeNullWhen(false)] out IPropertySymbol property)
    {
        // Sentinel for the false-return paths; MaybeNullWhen(false) documents that callers must not
        // read it unless the method returned true, so no call site needs a null-forgiving operator.
        property = null!;

        if (!TryExtractSingleParameterLambda(selector, out var parameter, out var body))
            return false;

        if (body is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } memberAccess)
            return false;

        if (context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is not { } parameterSymbol)
            return false;

        var receiverSymbol = context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol;
        if (!SymbolEqualityComparer.Default.Equals(receiverSymbol, parameterSymbol))
            return false;

        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IPropertySymbol resolved)
            return false;

        property = resolved;
        return true;
    }

    /// <summary>
    /// Returns whether <paramref name="property"/> is a Blazor <c>[Parameter]</c> with an accessible (public)
    /// setter, so a static <c>AddComponentParameter</c> setter can bind it without a runtime throw.
    /// </summary>
    private static bool IsSettableParameter(IPropertySymbol property, ComposableBodyContext context)
    {
        var parameterAttribute = context.KnownSymbols.ParameterAttributeType;
        if (parameterAttribute is null)
            return false;

        // Blazor resolves [Parameter] with inherit:true semantics (it walks the class hierarchy), so a
        // property that overrides a base [Parameter] without repeating the attribute is still a valid
        // parameter at runtime. Roslyn's GetAttributes() only sees directly-declared attributes, so walk
        // the override chain to match Blazor. `new`-shadowing has no OverriddenProperty, so a shadow
        // without its own [Parameter] correctly stops the walk and is rejected. Explicit interface
        // implementations never appear in this chain, which matches Blazor ignoring interface [Parameter]s.
        var hasParameterAttribute = false;
        for (var current = property; current is not null && !hasParameterAttribute; current = current.OverriddenProperty)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, parameterAttribute))
                {
                    hasParameterAttribute = true;
                    break;
                }
            }
        }

        return hasParameterAttribute && property.SetMethod is { DeclaredAccessibility: Accessibility.Public };
    }
}

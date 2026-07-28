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
    /// <summary>
    /// The parameter name <c>Component&lt;T&gt;(children)</c> binds to, matching Razor's rule that nested
    /// content becomes <c>ChildContent</c> and nothing else.
    /// </summary>
    private const string ChildContentParameterName = "ChildContent";
    public static RenderTemplateNode? Analyze(ExpressionSyntax expression, ComposableBodyContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        var expressionType = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;

        // Mixed content: a string-typed expression in any position (child, If branch, ForEach content)
        // becomes a bare text node. The pre-conversion Type is String even though it converts to View.
        if (expressionType is { SpecialType: SpecialType.System_String })
        {
            return new TextContentTemplateNode(ExpressionTemplateFactory.Create(expression, context));
        }

        // Same shape for an externally supplied RenderFragment: the pre-conversion Type is RenderFragment
        // even though it converts to View, and it lowers to the sibling AddContent overload. This must
        // stay ahead of the invocation guard below: a method call returning RenderFragment is neither an
        // Html factory nor a [Composable] call, so falling through would report BC1003.
        if (context.KnownSymbols.RenderFragmentType is { } renderFragmentType &&
            SymbolEqualityComparer.Default.Equals(expressionType, renderFragmentType))
        {
            return new RenderFragmentContentTemplateNode(ExpressionTemplateFactory.Create(expression, context));
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
            bool isElement = Is(method, symbols.HtmlElement);

            if (tag is not null || isElement)
            {
                if (FactoryArguments.Bind(invocation, context) is not { } args)
                    return null;

                if (isElement)
                {
                    if (args.At(0) is not { } tagArgument)
                        return null;

                    var tagArg = tagArgument.Expression;
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

                // One whole collection passed to the params parameter (Div(children: arr)) is not a list
                // of children; leave it unanalyzable so it lands on BC1003 instead of being mis-split.
                if (args.HasExplicitParamsArgument)
                    return null;

                var kids = AnalyzeChildren(args.ParamsElements, context);
                if (kids is null)
                    return null;

                return new ElementTemplateNode(tag!, default, default, default, kids.Value);
            }
        }

        if (Is(method, symbols.HtmlIf))
        {
            if (FactoryArguments.Bind(invocation, context) is not { } args)
                return null;

            if (args.At(0) is not { } conditionArg || args.At(1) is not { } thenArg)
                return null;

            var thenExpr = ExtractLambdaBody(thenArg.Expression);
            if (thenExpr is null)
                return null;

            var thenNode = Analyze(thenExpr, context);
            if (thenNode is null)
                return null;

            RenderTemplateNode? otherwiseNode = null;

            // Presence is now "an argument bound to the otherwise parameter", not "a third syntactic
            // argument", so If(cond, then: t) and If(cond, otherwise: o, then: t) both read correctly.
            // An explicitly passed null literal still means "no else branch".
            if (args.At(2) is { } otherwiseArg &&
                otherwiseArg.Expression is not LiteralExpressionSyntax
                { Token.RawKind: (int)SyntaxKind.NullKeyword })
            {
                var otherwiseExpr = ExtractLambdaBody(otherwiseArg.Expression);
                if (otherwiseExpr is null)
                    return null;

                otherwiseNode = Analyze(otherwiseExpr, context);
                if (otherwiseNode is null)
                    return null;
            }

            return new IfTemplateNode(
                ExpressionTemplateFactory.Create(conditionArg.Expression, context),
                thenNode,
                otherwiseNode);
        }

        if (Is(method, symbols.HtmlForEach))
        {
            if (FactoryArguments.Bind(invocation, context) is not { } args)
                return null;

            if (args.At(0) is not { } sourceArg ||
                args.At(1) is not { } keyArg ||
                args.At(2) is not { } contentArg)
            {
                return null;
            }

            if (!TryExtractSingleParameterLambda(keyArg.Expression, out var keyParameter, out var keyBody)
                || !TryExtractSingleParameterLambda(
                    contentArg.Expression, out var contentParameter, out var contentBody))
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
            var source = ExpressionTemplateFactory.Create(sourceArg.Expression, context);

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
                        keyArg.GetLocation(),
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

        bool isComponent = Is(method, symbols.HtmlComponent);
        bool isComponentWithChildren = Is(method, symbols.HtmlComponentWithChildren);
        if (isComponent || isComponentWithChildren)
        {
            // An unresolved type argument cannot be emitted: the display string of an unresolved type is
            // the written name with no qualification, and the generated file has no using directives, so
            // OpenComponent<T> would either fail with a CS0246 the author cannot reach or bind silently
            // to a different same-named type. Fail translation instead; the failure-path sweep in
            // ComponentModelFactory/ComposableDefinitionFactory then reports BC3012 once. Returning null
            // here also stops the Param branch from drawing a spurious BC3005 on the selector.
            if (UnresolvedComponentTypeScanner.ContainsUnresolvedType(method.TypeArguments[0]))
                return null;

            var typeName = method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (!isComponentWithChildren)
            {
                // Base case: Html.Component<T>() with no children and no .Param yet.
                return new ComponentTemplateNode(typeName, EquatableArray<ComponentParameter>.Empty);
            }

            if (FactoryArguments.Bind(invocation, context) is not { } componentArgs)
                return null;

            // One whole collection passed to the params parameter (Component<T>(children: arr)) is not a
            // list of children; leave it unanalyzable so it lands on BC1003 instead of being mis-split.
            if (componentArgs.HasExplicitParamsArgument)
                return null;

            var childNodes = AnalyzeChildren(componentArgs.ParamsElements, context);
            if (childNodes is null)
                return null;

            var slots = EquatableArray<ComponentSlot>.Empty;
            if (childNodes.Value.Length > 0)
            {
                if (!HasUsableChildContent(method.TypeArguments[0], context))
                {
                    context.Diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC3013,
                        invocation.GetLocation(),
                        [method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
                    return null;
                }

                // All children share the single ChildContent fragment; a Fragment node groups them
                // without emitting a wrapper element, matching how Razor lowers multiple nested children.
                RenderTemplateNode content = childNodes.Value.Length == 1
                    ? childNodes.Value[0]
                    : new FragmentTemplateNode(childNodes.Value);

                slots = ImmutableArray.Create(new ComponentSlot(ChildContentParameterName, content));
            }

            return new ComponentTemplateNode(
                typeName, EquatableArray<ComponentParameter>.Empty, slots);
        }

        if (Is(method, symbols.HtmlRaw))
        {
            if (FactoryArguments.Bind(invocation, context) is not { } args ||
                args.At(0) is not { } markupArg)
            {
                return null;
            }

            return new RawMarkupTemplateNode(
                ExpressionTemplateFactory.Create(markupArg.Expression, context));
        }

        if (Is(method, symbols.HtmlFragment))
        {
            if (FactoryArguments.Bind(invocation, context) is not { } args)
                return null;

            if (args.HasExplicitParamsArgument)
                return null;

            var children = AnalyzeChildren(args.ParamsElements, context);
            if (children is null)
                return null;

            return new FragmentTemplateNode(children.Value);
        }

        bool isScalarParam =
            SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, symbols.ParamMethod);
        bool isFragmentParam = symbols.FragmentParamMethod is not null
            && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, symbols.FragmentParamMethod);
        if (isScalarParam || isFragmentParam)
        {
            // Chained: <ComponentView<T> receiver>.Param(selector, value). Recurse into the receiver to
            // reach the base Component<T>() (or an inner .Param), then append this parameter in source order.
            if (invocation.Expression is not MemberAccessExpressionSyntax paramAccess
                || Analyze(paramAccess.Expression, context) is not ComponentTemplateNode inner)
            {
                return null;
            }

            var paramArgs = FactoryArguments.Bind(invocation, context);
            if (paramArgs is not { } args ||
                args.At(0) is not { } selectorArg ||
                args.At(1) is not { } valueArg)
            {
                return null;
            }

            var selector = selectorArg.Expression;
            var valueExpression = valueArg.Expression;

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

            // Duplicate detection spans BOTH channels and both directions: `null` binds to the scalar
            // overload (View is a struct, so `View v = null` is CS0037), so Component<T>(x) followed by
            // .Param(c => c.ChildContent, null) really can put one name in each channel.
            if (HasBinding(inner, property.Name))
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC3007, selector.GetLocation(), [property.Name]));
                return null;
            }

            if (isFragmentParam)
            {
                var slotContent = Analyze(valueExpression, context);
                if (slotContent is null)
                    return null;

                var appendedSlots = inner.Slots.AsImmutableArray()
                    .Add(new ComponentSlot(property.Name, slotContent));
                return new ComponentTemplateNode(inner.TypeName, inner.Parameters, appendedSlots);
            }

            if (isScalarParam && IsInertDesignTimeType(
                    context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).Type,
                    context))
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC3014,
                    valueExpression.GetLocation(),
                    [valueExpression.ToString()]));
                return null;
            }

            var value = ExpressionTemplateFactory.Create(valueExpression, context);
            var appended = inner.Parameters.AsImmutableArray().Add(new ComponentParameter(property.Name, value));
            return new ComponentTemplateNode(inner.TypeName, appended, inner.Slots);
        }

        // --- Decoration chain: class fold / attribute shortcut / generic .Attr / event shortcut / .On ---
        var normalized = KnownSymbols.Normalize(method);
        bool isClass = symbols.ClassMethod is not null
            && SymbolEqualityComparer.Default.Equals(normalized, KnownSymbols.Normalize(symbols.ClassMethod));
        bool isAttrShortcut = symbols.AttributeShortcuts.TryGetValue(normalized, out var shortcutAttrName);
        bool isEventShortcut = symbols.EventShortcuts.TryGetValue(normalized, out var shortcutEventName);
        bool isAttr = Contains(symbols.AttrMethods, normalized);
        bool isOn = Contains(symbols.OnMethods, normalized);

        if (isClass || isAttrShortcut || isEventShortcut || isAttr || isOn)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax decoAccess)
                return null;

            var inner = Analyze(decoAccess.Expression, context);
            // null: unanalyzable or already diagnosed — propagate silently (no double report).
            if (inner is null)
                return null;

            if (inner is not ElementTemplateNode element)
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC3008, decoAccess.Name.GetLocation(), []));
                return null;
            }

            if (FactoryArguments.Bind(invocation, context) is not { } args)
                return null;

            if (args.At(0) is not { } firstArg)
                return null;

            if (isClass)
            {
                return element with
                {
                    Classes = element.Classes.AsImmutableArray().Add(
                        ExpressionTemplateFactory.Create(firstArg.Expression, context)),
                };
            }

            if (isEventShortcut || isOn)
            {
                // Shortcut: name is implied; .On: name is arg[0] (constant required), handler is arg[1].
                string? eventName;
                ExpressionSyntax handlerExpr;
                if (isEventShortcut)
                {
                    eventName = shortcutEventName;
                    handlerExpr = firstArg.Expression;
                }
                else if (TryGetConstantName(firstArg.Expression, context, out eventName))
                {
                    if (args.At(1) is not { } secondArg)
                        return null;

                    handlerExpr = secondArg.Expression;
                }
                else
                {
                    context.Diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC3011, firstArg.GetLocation(), []));
                    return null;
                }

                foreach (var existing in element.Events.AsImmutableArray())
                {
                    if (string.Equals(existing.Name, eventName, System.StringComparison.Ordinal))
                    {
                        context.Diagnostics.Add(DiagnosticInfo.Create(
                            DiagnosticDescriptors.BC3010, decoAccess.Name.GetLocation(), [eventName!]));
                        return null;
                    }
                }

                return element with
                {
                    Events = element.Events.AsImmutableArray().Add(
                        new EventTemplate(eventName!, ExpressionTemplateFactory.Create(handlerExpr, context))),
                };
            }

            // Attribute shortcut or generic .Attr.
            string? attrName;
            ExpressionSyntax valueExpr;
            if (isAttrShortcut)
            {
                attrName = shortcutAttrName;
                valueExpr = firstArg.Expression;
            }
            else if (TryGetConstantName(firstArg.Expression, context, out attrName))
            {
                if (args.At(1) is not { } secondArg)
                    return null;

                valueExpr = secondArg.Expression;
            }
            else
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BC3011, firstArg.GetLocation(), []));
                return null;
            }

            var value = ExpressionTemplateFactory.Create(valueExpr, context);

            // 'class' folds (case-sensitive, ordinal) — same channel as .Class, may repeat.
            if (string.Equals(attrName, "class", System.StringComparison.Ordinal))
            {
                return element with { Classes = element.Classes.AsImmutableArray().Add(value) };
            }

            foreach (var existing in element.Attributes.AsImmutableArray())
            {
                if (string.Equals(existing.Name, attrName, System.StringComparison.Ordinal))
                {
                    context.Diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticDescriptors.BC3010, decoAccess.Name.GetLocation(), [attrName!]));
                    return null;
                }
            }

            return element with
            {
                Attributes = element.Attributes.AsImmutableArray().Add(new AttributeTemplate(attrName!, value)),
            };
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

    private static bool Contains(IReadOnlyCollection<ISymbol> set, ISymbol symbol)
    {
        foreach (var s in set)
        {
            if (SymbolEqualityComparer.Default.Equals(s, symbol))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether <paramref name="node"/> already binds <paramref name="name"/> in either channel. Blazor
    /// applies the last write, so a duplicate across channels is as dead as one within a channel.
    /// </summary>
    private static bool HasBinding(ComponentTemplateNode node, string name)
    {
        foreach (var parameter in node.Parameters.AsImmutableArray())
        {
            if (string.Equals(parameter.Name, name, System.StringComparison.Ordinal))
                return true;
        }

        foreach (var slot in node.Slots.AsImmutableArray())
        {
            if (string.Equals(slot.Name, name, System.StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Analyzes each child expression into a child template node, returning null if any child
    /// cannot be statically analyzed (propagated as translation failure).</summary>
    private static ImmutableArray<RenderTemplateNode>? AnalyzeChildren(
        ImmutableArray<ExpressionSyntax> children, ComposableBodyContext context)
    {
        var nodes = ImmutableArray.CreateBuilder<RenderTemplateNode>(children.Length);
        foreach (var child in children)
        {
            var node = Analyze(child, context);
            if (node is null)
                return null;

            nodes.Add(node);
        }

        return nodes.ToImmutable();
    }

    private static bool TryGetConstantName(
        ExpressionSyntax expression, ComposableBodyContext context, out string? name)
    {
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant is { HasValue: true, Value: string value } && !string.IsNullOrWhiteSpace(value))
        {
            name = value;
            return true;
        }
        name = null;
        return false;
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
                // Ordinarily argument.Syntax IS the ArgumentSyntax. But when the argument expression is
                // a bare null-forgiving suppression with nothing else to convert (e.g. `Target(value!)`),
                // Roslyn elides the suppression operator from the operation tree and Syntax points at the
                // innermost operand instead — so look for the enclosing ArgumentSyntax rather than
                // requiring an exact cast. Mirrors FactoryArguments.Bind's default arm, which the Html
                // factory path already uses for the same elision.
                var argumentExpression = argument.Syntax.FirstAncestorOrSelf<ArgumentSyntax>()?.Expression;
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

    /// <summary>
    /// Whether <paramref name="componentType"/> declares a <c>ChildContent</c> member that can receive
    /// child content: a settable <c>[Parameter]</c> whose type is exactly the non-generic
    /// <c>RenderFragment</c>. A <c>RenderFragment&lt;TContext&gt;</c> is excluded deliberately — the
    /// generated lambda is non-generic and would fail an invalid cast at runtime.
    /// </summary>
    private static bool HasUsableChildContent(ITypeSymbol componentType, ComposableBodyContext context)
    {
        if (context.KnownSymbols.RenderFragmentType is not { } renderFragmentType)
            return false;

        for (var current = componentType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(ChildContentParameterName))
            {
                if (member is not IPropertySymbol property)
                    continue;

                if (!SymbolEqualityComparer.Default.Equals(property.Type, renderFragmentType))
                    continue;

                if (IsSettableParameter(property, context))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is one of the inert design-time markers (<c>View</c> or a
    /// <c>ComponentView&lt;T&gt;</c> construction). The generic Param emits its value verbatim, so such a
    /// value would bind the empty marker instead of content.
    /// </summary>
    private static bool IsInertDesignTimeType(ITypeSymbol? type, ComposableBodyContext context)
    {
        if (type is null)
            return false;

        var symbols = context.KnownSymbols;

        if (symbols.ViewType is { } viewType && SymbolEqualityComparer.Default.Equals(type, viewType))
            return true;

        return symbols.ComponentViewType is { } componentViewType
            && type is INamedTypeSymbol named
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, componentViewType);
    }
}

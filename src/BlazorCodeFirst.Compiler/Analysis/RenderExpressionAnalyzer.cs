using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using BlazorCodeFirst.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Classifies a composable definition body expression into the statically sequenceable
/// <see cref="RenderTemplateNode"/> hierarchy. Dynamic argument text is normalized through
/// <see cref="ExpressionTemplateFactory"/> so parameter references become holes and imports/containing
/// type context are preserved. Nested <c>[Composable]</c> calls become <see cref="ComposableCallTemplateNode"/>.
/// Returns <see langword="null"/> when the expression cannot be statically analyzed.
/// </summary>
internal static class RenderExpressionAnalyzer
{
    /// <summary>
    /// The parameter name <c>Component&lt;T&gt;()[children]</c> binds to, matching Razor's rule that nested
    /// content becomes <c>ChildContent</c> and nothing else.
    /// </summary>
    private const string ChildContentParameterName = "ChildContent";

    /// <summary>
    /// <c>EventCallback.Factory.Create&lt;TValue&gt;</c> up to its type argument, which a component
    /// binding's <c>{name}Changed</c> parameter carries. Written in the instance syntax Razor uses:
    /// <c>Create</c> is a real instance method on <c>EventCallbackFactory</c>, not an extension method,
    /// so the absence of <c>using</c> directives in the generated file costs it nothing — unlike
    /// <c>CreateBinder</c>, which <see cref="RenderViewEmitter"/> has to spell as the static call it is.
    /// This is the same spelling the event channel emits.
    /// </summary>
    private const string CreateCall =
        "global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<";

    /// <summary>
    /// Fully qualified with no special-type spellings, so <c>string</c> is written
    /// <c>global::System.String</c>. <see cref="SymbolDisplayFormat.FullyQualifiedFormat"/> carries
    /// <see cref="SymbolDisplayMiscellaneousOptions.UseSpecialTypes"/>, which would emit the keyword and
    /// leave the one cast this file writes depending on a language keyword rather than on a qualified
    /// name.
    /// </summary>
    private static readonly SymbolDisplayFormat FullyQualifiedTypeName =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    /// <summary>
    /// Classifies <paramref name="expression"/>, recording it on <paramref name="context"/> when it cannot
    /// be classified. Every recursive descent goes through here rather than through
    /// <see cref="Classify"/>, so the innermost failure is the one recorded and BCF1003 can name the
    /// construct the author actually wrote instead of the whole design-time expression.
    /// </summary>
    public static RenderTemplateNode? Analyze(ExpressionSyntax expression, ComposableBodyContext context)
    {
        var node = Classify(expression, context);
        if (node is null)
            context.RecordUntranslatable(expression);

        return node;
    }

    private static RenderTemplateNode? Classify(ExpressionSyntax expression, ComposableBodyContext context)
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
        // stay ahead of the invocation guard below: a method call returning RenderFragment is neither
        // design-time syntax nor a [Composable] call, so falling through would report BCF1003.
        if (context.KnownSymbols.RenderFragmentType is { } renderFragmentType &&
            SymbolEqualityComparer.Default.Equals(expressionType, renderFragmentType))
        {
            return new RenderFragmentContentTemplateNode(ExpressionTemplateFactory.Create(expression, context));
        }

        // Prefilter on syntax kind before asking for a symbol, so a literal, a field reference or a lambda
        // does not each pay a semantic query on the way to returning null.
        if (expression is not (InvocationExpressionSyntax or ElementAccessExpressionSyntax
                or IdentifierNameSyntax or MemberAccessExpressionSyntax))
        {
            return null;
        }

        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        var symbols = context.KnownSymbols;

        // Dispatch on the resolved symbol rather than the syntax kind. A childless element written under
        // `using static` is an IdentifierNameSyntax and the qualified escape hatch (Html.Img) is a
        // MemberAccessExpressionSyntax; both resolve to the same property, so one arm serves both spellings
        // and neither can be dropped by matching syntax.
        if (symbol is IPropertySymbol resolvedProperty)
        {
            if (!resolvedProperty.IsIndexer)
            {
                // A childless element has no bracket form at all: `Div[]` is CS0443, so the two shapes per
                // element are unavoidable and this is the one that carries no children. Asked first because it
                // is the overwhelmingly common answer -- every Div, Span and P in every body arrives here --
                // and the two arms are disjoint: an element helper returns ElementView and Slot is View.
                if (symbols.ElementTags.TryGetValue(
                        KnownSymbols.Normalize(resolvedProperty), out var propertyTag))
                {
                    return new ElementTemplateNode(propertyTag);
                }

                return symbols.IsSlot(resolvedProperty)
                    ? ClassifySlot(expression, symbols.SlotProperty!, context)
                    : null;
            }

            return expression is ElementAccessExpressionSyntax elementAccess
                ? ClassifyIndexer(elementAccess, resolvedProperty, context)
                : null;
        }

        // A reference to one of the definition's own View-typed parameters: an additional content slot (#34).
        // The kind comes from ResolveHole rather than from the parameter's type, so a View-typed *render
        // variable* -- a ForEach over a sequence of Views binds one -- stays the value it is.
        if (symbol is IParameterSymbol
            && context.ResolveHole(symbol, out var contentOrdinal) == BodyHoleKind.Content)
        {
            return new ContentHoleTemplateNode(contentOrdinal);
        }

        // The method arm still requires an invocation. The early return this replaced also filtered method
        // groups (Body => SomeMethodReturningRenderFragment), whose GetTypeInfo().Type is null so the
        // RenderFragment arm above does not catch them either; without this condition such a group would
        // reach the arms below and have arguments read off a call that was never written.
        if (expression is not InvocationExpressionSyntax invocation || symbol is not IMethodSymbol method)
            return null;

        // One arm per SurfaceMethodKind, dispatching on the single lookup that says which method of the
        // design-time surface this is. A switch expression rather than the chain of predicates it
        // replaces: a kind added to the enum without an arm here stops the compiler, where a predicate
        // missing from one copy of that chain used to leave the method silently unhandled (#191, #201).
        var kind = symbols.ClassifySurfaceMethod(method);

        // CS8524 asks for a discard arm to cover an integer cast into the enum, and a discard arm is
        // exactly what would silence CS8509 — the error that makes an added kind impossible to forget.
        // Every value reaching here comes from KnownSymbols' own table, so the case it warns about has no
        // route in, and the check worth keeping is the other one.
#pragma warning disable CS8524
        return kind switch
        {
            SurfaceMethodKind.Element => ClassifyElementFactory(invocation, context),
            SurfaceMethodKind.If => ClassifyIf(invocation, context),
            SurfaceMethodKind.ForEach => ClassifyForEach(invocation, context),
            SurfaceMethodKind.Component => ClassifyComponentFactory(method),
            SurfaceMethodKind.Raw => ClassifyRaw(invocation, context),
            SurfaceMethodKind.Fragment => ClassifyFragment(invocation, context),
            SurfaceMethodKind.ScalarParam
                or SurfaceMethodKind.FragmentParam
                or SurfaceMethodKind.GenericTemplateIgnored
                or SurfaceMethodKind.GenericTemplateContextual
                or SurfaceMethodKind.ComponentBind =>
                ClassifyComponentParameter(invocation, method, kind, context),
            SurfaceMethodKind.Class
                or SurfaceMethodKind.AttributeShortcut
                or SurfaceMethodKind.EventShortcut
                or SurfaceMethodKind.Attr
                or SurfaceMethodKind.On
                or SurfaceMethodKind.Bind =>
                ClassifyDecoration(invocation, method, kind, context),
            SurfaceMethodKind.None => ClassifyComposableCall(invocation, method, context),
        };
#pragma warning restore CS8524
    }

    /// <summary>
    /// <c>Element(tag)</c>, the escape hatch for a tag outside the curated table. It carries no children
    /// of its own, those are written in brackets on the <c>ElementView</c> it returns, which arrives at
    /// <see cref="Classify"/> as an element access and not an invocation, so this arm only has to resolve
    /// the tag.
    /// </summary>
    private static ElementTemplateNode? ClassifyElementFactory(
        InvocationExpressionSyntax invocation, ComposableBodyContext context)
    {
        if (FactoryArguments.Bind(invocation, context) is not { } args
            || args.At(0) is not { } tagArgument)
        {
            return null;
        }

        var tagArg = tagArgument.Expression;
        var constant = context.SemanticModel.GetConstantValue(tagArg, context.CancellationToken);
        if (constant is not { HasValue: true, Value: string tagValue }
            || string.IsNullOrWhiteSpace(tagValue))
        {
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3009, tagArg.GetLocation(), []));
            return null;
        }

        return new ElementTemplateNode(tagValue);
    }

    private static IfTemplateNode? ClassifyIf(
        InvocationExpressionSyntax invocation, ComposableBodyContext context)
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

    private static ForEachTemplateNode? ClassifyForEach(
        InvocationExpressionSyntax invocation, ComposableBodyContext context)
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
                DiagnosticDescriptors.BCF3004,
                invocation.GetLocation(),
                []));
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(keyParameter, context.CancellationToken) is not { } keyParamSymbol
            || context.SemanticModel.GetDeclaredSymbol(contentParameter, context.CancellationToken) is not { } contentParamSymbol)
        {
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3004,
                invocation.GetLocation(),
                []));
            return null;
        }

        // Source references the enclosing scope (fields, composable params, outer items), never this
        // item, so it is normalized before the iteration variable is registered.
        var source = ExpressionTemplateFactory.Create(sourceArg.Expression, context);

        var itemOrdinal = context.PushRenderVariable(contentParamSymbol, keyParamSymbol);
        try
        {
            var key = ExpressionTemplateFactory.Create(keyBody, context);
            var content = Analyze(contentBody, context);
            if (content is null)
                return null;

            if (!KeyReferencesItemOrdinal(key, itemOrdinal))
            {
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3002,
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
            context.PopRenderVariable(contentParamSymbol, keyParamSymbol);
        }
    }

    private static ComponentTemplateNode? ClassifyComponentFactory(IMethodSymbol method)
    {
        // An unresolved type argument cannot be emitted: the display string of an unresolved type is
        // the written name with no qualification, and the generated file has no using directives, so
        // OpenComponent<T> would either fail with a CS0246 the author cannot reach or bind silently
        // to a different same-named type. Fail translation instead; the failure-path sweep in
        // ComponentModelFactory/ComposableDefinitionFactory then reports BCF3012 once. Returning null
        // here also stops the Param branch from drawing a spurious BCF3005 on the selector.
        if (TypeSymbolFacts.ContainsUnresolvedType(method.TypeArguments[0]))
            return null;

        // Base case: Html.Component<T>() with no children and no .Param yet. Children and parameters
        // both arrive on the ComponentView<T> this returns, through its indexer and .Param, so this
        // arm never sees either.
        return new ComponentTemplateNode(
            method.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EquatableArray<ComponentParameter>.Empty);
    }

    private static RawMarkupTemplateNode? ClassifyRaw(
        InvocationExpressionSyntax invocation, ComposableBodyContext context)
    {
        if (FactoryArguments.Bind(invocation, context) is not { } args ||
            args.At(0) is not { } markupArg)
        {
            return null;
        }

        return new RawMarkupTemplateNode(
            ExpressionTemplateFactory.Create(markupArg.Expression, context));
    }

    private static FragmentTemplateNode? ClassifyFragment(
        InvocationExpressionSyntax invocation, ComposableBodyContext context)
    {
        if (FactoryArguments.Bind(invocation, context) is not { } args)
            return null;

        if (args.HasUnanalyzableParamsArgument)
            return null;

        var children = AnalyzeChildren(args.ParamsElements, context);
        if (children is null)
            return null;

        return new FragmentTemplateNode(children.Value);
    }

    /// <summary>
    /// A <c>.Param</c>, <c>.Template</c> or <c>.Bind</c> written on a component, <paramref name="kind"/>
    /// saying which.
    /// </summary>
    private static ComponentTemplateNode? ClassifyComponentParameter(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SurfaceMethodKind kind,
        ComposableBodyContext context)
    {
        var symbols = context.KnownSymbols;

        // Chained: <ComponentView<T> receiver>.Param/Template(selector, value), or
        // .Bind(selector, get[, set]). Recurse into the receiver to reach the base Component<T>() (or
        // an inner parameter call), then append this binding in source order. All spellings share
        // everything up to the selected property: they take the same selector in the same position
        // and answer to the same three rules about it.
        if (invocation.Expression is not MemberAccessExpressionSyntax paramAccess
            || Analyze(paramAccess.Expression, context) is not ComponentTemplateNode inner)
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            return null;
        }

        // Parameter 1 is .Param's value and .Bind's getter; both are required, so one check serves.
        var paramArgs = FactoryArguments.Bind(invocation, context);
        if (paramArgs is not { } args ||
            args.At(0) is not { } selectorArg ||
            args.At(1) is not { } valueArg)
        {
            return null;
        }

        var selector = selectorArg.Expression;

        if (!TryGetSelectorProperty(selector, context, out var property))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3005, selector.GetLocation(), []));
            return null;
        }

        if (!IsSettableParameter(property, context))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3006, selector.GetLocation(), [property.Name]));
            return null;
        }

        // Duplicate detection spans BOTH channels, not just the parameter one: `null` binds to the
        // scalar overload (View is a struct, so `View v = null` is CS0037), so
        // .Param(c => c.ChildContent, Div["y"]).Param(c => c.ChildContent, null) really can put one
        // name in each channel. The children-then-.Param direction cannot reach here, the indexer
        // returns View, so nothing follows the brackets, and is checked by ClassifyComponentIndexer.
        if (HasBinding(inner, property.Name))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3007, selector.GetLocation(), [property.Name]));
            return null;
        }

        if (kind == SurfaceMethodKind.ComponentBind)
            return ClassifyComponentBind(invocation, method, inner, property, selector, args, valueArg, context);

        var valueExpression = valueArg.Expression;

        if (kind == SurfaceMethodKind.FragmentParam)
        {
            var slotContent = Analyze(valueExpression, context);
            if (slotContent is null)
                return null;

            return AppendSlot(inner, property.Name, slotContent);
        }

        if (kind == SurfaceMethodKind.GenericTemplateIgnored)
        {
            if (!TryGetFragmentContextTypeName(property, symbols, out var contextTypeName))
                return null;

            var slotContent = Analyze(valueExpression, context);
            if (slotContent is null)
                return null;

            return AppendSlot(
                inner,
                property.Name,
                slotContent,
                ComponentSlotKind.GenericContextIgnored,
                contextTypeName);
        }

        if (kind == SurfaceMethodKind.GenericTemplateContextual)
        {
            if (!TryGetFragmentContextTypeName(property, symbols, out var contextTypeName))
                return null;

            // The content has to be an inline expression lambda twice over: the body is what gets
            // sequenced, and the parameter symbol is what the generated context variable is
            // substituted for. A method group, an anonymous method, and a block-bodied lambda supply
            // neither. Arity is not checked here: a lambda with no parameter or with two does not
            // convert to Func<TContext, View>, so C# has already rejected the call.
            if (!TryExtractSingleParameterLambda(
                    valueExpression, out var contextParameter, out var contextBody)
                || context.SemanticModel.GetDeclaredSymbol(
                    contextParameter, context.CancellationToken) is not { } contextParameterSymbol)
            {
                context.RejectUnresolvedValueRecovery(invocation.Span);
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3022, valueArg.GetLocation(), []));
                return null;
            }

            context.PushRenderVariable(contextParameterSymbol);
            try
            {
                var slotContent = Analyze(contextBody, context);
                if (slotContent is null)
                    return null;

                return AppendSlot(
                    inner,
                    property.Name,
                    slotContent,
                    ComponentSlotKind.GenericContextual,
                    contextTypeName);
            }
            finally
            {
                context.PopRenderVariable(contextParameterSymbol);
            }
        }

        if (kind == SurfaceMethodKind.ScalarParam && IsInertDesignTimeType(
                context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken).Type,
                context))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3014,
                valueExpression.GetLocation(),
                [valueExpression.ToString()]));
            return null;
        }

        var value = ExpressionTemplateFactory.Create(valueExpression, context);
        var appended = inner.Parameters.AsImmutableArray().Add(new ComponentParameter(property.Name, value));
        return new ComponentTemplateNode(inner.TypeName, appended, inner.Slots);
    }

    /// <summary>
    /// A decoration written onto an element: the class fold, an attribute shortcut, the generic
    /// <c>.Attr</c>, an event shortcut, <c>.On</c>, or <c>.Bind</c>.
    /// </summary>
    private static ElementTemplateNode? ClassifyDecoration(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SurfaceMethodKind kind,
        ComposableBodyContext context)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax decoAccess)
            return null;

        var inner = Analyze(decoAccess.Expression, context);
        // null: unanalyzable or already diagnosed, propagate silently (no double report).
        if (inner is null)
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            return null;
        }

        // Unreachable: a decoration takes an ElementView receiver, so anything that opens no element
        // frame is a CS1929 and never resolves to a decoration here. Kept so that if some route ever does
        // arrive, translation fails safely instead of decorating a node that cannot carry attributes.
        if (inner is not ElementTemplateNode element)
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            return null;
        }

        if (FactoryArguments.Bind(invocation, context) is not { } args)
            return null;

        if (args.At(0) is not { } firstArg)
            return null;

        if (kind == SurfaceMethodKind.Bind)
            return ClassifyBind(invocation, decoAccess, method, element, args, firstArg, context);

        // The name a named shortcut stands for, or null for the .Attr and .On spellings that take it as an
        // argument. The lookup cannot miss: the kind and the map entry are written by the same arm of
        // KnownSymbols' member switch.
        var normalized = KnownSymbols.Normalize(method);
        var shortcutName = kind switch
        {
            SurfaceMethodKind.AttributeShortcut => context.KnownSymbols.AttributeShortcuts[normalized],
            SurfaceMethodKind.EventShortcut => context.KnownSymbols.EventShortcuts[normalized],
            _ => null,
        };

        if (kind is SurfaceMethodKind.EventShortcut or SurfaceMethodKind.On)
        {
            // Which argument carries the handler is asked of KnownSymbols, not assumed from the position
            // this arm happens to have been written against (#221). The event's name comes either from the
            // classification's shortcut table or from the first argument, never from both and never from
            // neither, so exactly one of the two must be present. That is pinned rather than assumed
            // because the resolver below reads the name out of firstArg: requiring the index to be 0, and
            // not merely to exist, is what keeps a widened TryGetEventParameters from moving the name
            // somewhere this arm would go on reading past.
            if (!KnownSymbols.TryGetEventParameters(method, out var eventParameters)
                || (shortcutName is not null) == (eventParameters.EventNameIndex == 0))
            {
                context.RejectUnresolvedValueRecovery(invocation.Span);
                return null;
            }

            if (!TryResolveDecorationName(invocation, firstArg, shortcutName, context, out var eventName))
                return null;

            // No event overload declares an optional handler, so a missing one is a call this compiler was
            // not written against. Returning null without reporting sends the body to BCF1003, which is
            // where an unrecognized call belongs; the attribute channel's own reading of "no argument" is
            // deliberately different (#178), which is why neither is in the shared resolver.
            if (args.At(eventParameters.HandlerIndex) is not { } handlerArgument)
                return null;

            var handlerExpr = handlerArgument.Expression;

            // The event-shortcut path supplies its own name from a literal table and never reaches
            // here with a bad one, so only the .On / .Bind string path is checked.
            if (kind != SurfaceMethodKind.EventShortcut
                && !eventName.StartsWith("on", System.StringComparison.Ordinal))
            {
                context.RejectUnresolvedValueRecovery(invocation.Span);
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3019, firstArg.GetLocation(), [eventName]));
                return null;
            }

            if (HasBinding(element, eventName))
            {
                context.RejectUnresolvedValueRecovery(invocation.Span);
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3010, decoAccess.Name.GetLocation(), [eventName]));
                return null;
            }

            return element with
            {
                Events = element.Events.AsImmutableArray().Add(
                    new EventTemplate(eventName, ExpressionTemplateFactory.Create(handlerExpr, context))),
            };
        }

        // .Class, an attribute shortcut, or the generic .Attr. The value is the last parameter — the name,
        // where it is written at all, is the one ahead of it — and this one derivation answers both readings
        // below: the argument index the value is carried at, and the value's type, which is what the class
        // channel admits or refuses on. Deriving it once is what puts .Class and .Attr("class", …) under the
        // same admission, where the type used to be read on the .Attr route alone (#193).
        //
        // .Attr(name) is the one spelling with no value parameter at all: the last parameter is the name,
        // and reading it as the value would hand the class channel a string spelled "class" and emit
        // class="class" (#178). Named here rather than guarded at each reader, so the two questions below
        // are still asked once.
        var attributeValue = HasValueParameter(method, kind)
            ? method.Parameters[method.Parameters.Length - 1]
            : null;

        // The presence the bare spelling stands for. A bool, so `class` refuses it as BCF3023 exactly as the
        // written bool overload does, and so the fold writes it as name="" through the branch that overload
        // already goes through.
        var attributeValueType = attributeValue?.Type
            ?? context.SemanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);

        // .Class is the channel and carries no name to resolve, so it routes before the name is read.
        if (kind == SurfaceMethodKind.Class)
        {
            return FoldIntoClassChannel(
                invocation, decoAccess, element, attributeValueType,
                new AttributeValueSource(firstArg.Expression, firstArg.GetLocation()), context);
        }

        if (!TryResolveDecorationName(invocation, firstArg, shortcutName, context, out var attrName))
            return null;

        if (!TryResolveAttributeValue(args, decoAccess, attributeValue, out var value))
            return null;

        // 'class' routes to the channel rather than to Attributes, the same as .Class, and may repeat.
        // The shortcut route never spells the name, so only .Attr arrives here.
        if (ClassChannel.Owns(attrName))
            return FoldIntoClassChannel(invocation, decoAccess, element, attributeValueType, value, context);

        // Reject before normalizing the value, as the event channel does: normalization reports on the
        // value's own types, and a rejected decoration's value never reaches generated code.
        if (HasBinding(element, attrName))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3010, decoAccess.Name.GetLocation(), [attrName]));
            return null;
        }

        return element with
        {
            Attributes = element.Attributes.AsImmutableArray().Add(
                new AttributeTemplate(attrName, value.Normalize(context))),
        };
    }

    /// <summary>
    /// Whether the resolved decoration declares a parameter for its value. Every spelling does except
    /// <c>.Attr(name)</c>, the bare form of a valueless attribute (#178), whose only parameters are the
    /// receiver and the name.
    /// </summary>
    /// <remarks>
    /// Asked of the parameter count rather than of the argument count, so a named argument answers the same.
    /// The receiver is skipped through <see cref="KnownSymbols.ReceiverOffset(IMethodSymbol)"/> rather than
    /// assumed present: the symbol reaching here is reduced for the fluent spelling
    /// (<c>view.Attr("disabled")</c>) and unreduced for the static one
    /// (<c>Decorations.Attr(view, "disabled")</c>), so a count compared against a literal would read one of
    /// the two as the wrong arity — and read <em>every</em> <c>.Attr</c> as valueless in the fluent case,
    /// which is what a first draft of this did.
    /// </remarks>
    private static bool HasValueParameter(IMethodSymbol method, SurfaceMethodKind kind) =>
        kind != SurfaceMethodKind.Attr
            || method.Parameters.Length - KnownSymbols.ReceiverOffset(method) > 1;

    /// <summary>
    /// Where an attribute-channel decoration's value came from, before it is normalized: the expression the
    /// author wrote, or nothing at all for the one spelling that declares no value parameter.
    /// </summary>
    /// <remarks>
    /// Normalization is deliberately not done on construction. It is what reports BCF3015 for a value whose
    /// type cannot be emitted, and a decoration rejected for some other reason — a duplicate binding — never
    /// reaches generated code, so its value's type is not the author's problem. Carrying the source and
    /// normalizing at the point each caller has finished refusing is what keeps that ownership
    /// (<c>UnresolvedEmittedTypeTests.DuplicateAttribute_UnresolvedValueType_RemainsBCF3010Owned</c>).
    /// </remarks>
    /// <param name="Written">
    /// The written value expression, or <see langword="null"/> for <c>.Attr(name)</c>.
    /// </param>
    /// <param name="Location">
    /// Where a rule about the value reports. The written argument, or the decoration's own name when there
    /// is no argument to point at.
    /// </param>
    private readonly record struct AttributeValueSource(ExpressionSyntax? Written, Location Location)
    {
        /// <summary>
        /// The value as a template. <c>.Attr(name)</c> means the attribute is present with no value, which
        /// is <c>.Attr(name, true)</c> in every respect but how it reads (#178), so it becomes that same
        /// constant here. Synthesizing it in one place is what keeps the two spellings on one path:
        /// everything downstream — the class channel's admission, the fold's <c>name=""</c> branch, the
        /// emitted frame — sees the same constant either way, so they cannot translate differently.
        /// </summary>
        public ExpressionTemplate Normalize(ComposableBodyContext context) =>
            Written is null
                ? ExpressionTemplateFactory.ForBooleanConstant(true)
                : ExpressionTemplateFactory.Create(Written, context);
    }

    /// <summary>
    /// Reads where an attribute-channel decoration's value comes from, or fails for a call this compiler was
    /// not written against (a value parameter that received no argument and has no default).
    /// </summary>
    private static bool TryResolveAttributeValue(
        FactoryArguments args,
        MemberAccessExpressionSyntax decoAccess,
        IParameterSymbol? valueParameter,
        [MaybeNullWhen(false)] out AttributeValueSource source)
    {
        if (valueParameter is null)
        {
            source = new AttributeValueSource(Written: null, decoAccess.Name.GetLocation());
            return true;
        }

        if (args.At(KnownSymbols.ArgumentIndex(valueParameter)) is { } written)
        {
            source = new AttributeValueSource(written.Expression, written.GetLocation());
            return true;
        }

        source = default;
        return false;
    }

    /// <summary>
    /// <c>Html.Slot</c>: the hole where a content-taking <c>[Composable]</c> places its caller's brackets.
    /// Translatable only inside such a body, where the definition bound it to an ordinal; anywhere else it
    /// is BCF3025 (#176).
    /// </summary>
    /// <remarks>
    /// The two rejected cases both land here. A component's <c>Body</c> or <c>Chrome</c> is analyzed with an
    /// empty parameter map, and a <c>[Composable]</c> returning <c>View</c> registers no slot ordinal, so
    /// neither can look one up — and the message says which by naming what a slot requires rather than
    /// guessing at the author's intent. The other arity failures, zero and two, are reported at the
    /// declaration by <c>ComposableDefinitionFactory</c>, which is where the count is a property of.
    /// </remarks>
    private static ContentHoleTemplateNode? ClassifySlot(
        ExpressionSyntax expression, IPropertySymbol slotProperty, ComposableBodyContext context)
    {
        if (context.ResolveHole(slotProperty, out var slotOrdinal) == BodyHoleKind.Content)
            return new ContentHoleTemplateNode(slotOrdinal);

        context.Diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticDescriptors.BCF3025,
            expression.GetLocation(),
            ["is written where no caller content is received"]));
        return null;
    }

    /// <summary>
    /// A call that is not surface syntax at all, which is a translatable expression only when the method
    /// it names carries <c>[Composable]</c>. That attribute sits on a user method rather than on a symbol
    /// resolved out of the runtime, so it cannot be part of the classification and is tested here.
    /// </summary>
    private static ComposableCallTemplateNode? ClassifyComposableCall(
        InvocationExpressionSyntax invocation, IMethodSymbol method, ComposableBodyContext context)
    {
        if (!IsComposable(method, context))
            return null;

        var arguments = CreateInvocationArguments(invocation, method, context, out var contentArguments);
        if (arguments is null)
            return null;

        return new ComposableCallTemplateNode(
            MethodKey.Create(method),
            method.Name,
            arguments.Value,
            TemplateLocation.From(invocation.GetLocation()))
        {
            ContentArguments = contentArguments,
        };
    }

    /// <summary>
    /// Offers <paramref name="value"/> to <paramref name="element"/>'s class channel, adding it when the
    /// channel admits it and reporting the refusal otherwise.
    /// </summary>
    /// <param name="valueType">
    /// The type of the value parameter on the resolved overload, which is what the channel admits or
    /// refuses on. Passed rather than derived here, because the caller has already had to derive it to
    /// find the value argument at all.
    /// </param>
    /// <remarks>
    /// Both spellings that fold — <c>.Class</c> and <c>.Attr("class", …)</c> — arrive here, so every rule
    /// about the channel is asked once and the answer does not depend on which of them was used.
    /// <see cref="ClassChannel.Admit"/> holds the rules; this method only maps a refusal onto the
    /// diagnostic and the location that report it. It is also the half of BCF3024 that catches the
    /// decorations written in the other order: a <c>.Class</c> never reaches
    /// <see cref="ClassifyBind"/>, so the bind arm alone would report only the chains where the binding
    /// came last.
    /// </remarks>
    private static ElementTemplateNode? FoldIntoClassChannel(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax decoAccess,
        ElementTemplateNode element,
        ITypeSymbol valueType,
        AttributeValueSource value,
        ComposableBodyContext context)
    {
        switch (ClassChannel.Admit(element, valueType))
        {
            case ClassChannelAdmission.ValueDoesNotJoin:
                context.RejectUnresolvedValueRecovery(invocation.Span);
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3023, value.Location, []));
                return null;

            case ClassChannelAdmission.NameAlreadyBound:
                context.RejectUnresolvedValueRecovery(invocation.Span);
                context.Diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticDescriptors.BCF3024, decoAccess.Name.GetLocation(), []));
                return null;
        }

        return element with
        {
            Classes = element.Classes.AsImmutableArray().Add(value.Normalize(context)),
        };
    }

    /// <summary>
    /// Classifies <c>.Bind(attribute, event, get)</c> and its explicit-setter form onto
    /// <paramref name="element"/>, or reports why it cannot be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every <em>reported</em> rejection registers a
    /// <see cref="ComposableBodyContext.RejectUnresolvedValueRecovery"/> span for the whole invocation, as
    /// the sibling decoration arms do: the getter and setter are dynamic argument expressions, and a
    /// rejected binding never reaches generated code, so the failure scanner must not go on to report on
    /// their types. The defensive <see langword="null"/> returns (a missing argument, and a
    /// <see cref="KnownSymbols.TryGetBindParameters"/> that declines) do not, and need not: each answers a
    /// call this compiler was not written against, none of them reports anything of its own, and the
    /// unregistered span costs at most a second report on a body that is already BCF1003.
    /// </para>
    /// <para>
    /// One shape passes every check and is deliberately not diagnosed:
    /// <c>.Bind("oninput", "oninput", …)</c>, the same name in both positions, emits two frames under one
    /// name of which the second wins. It is not written by accident — a swapped pair, which is, is caught
    /// by BCF3019 — so it buys no check of its own.
    /// </para>
    /// </remarks>
    private static ElementTemplateNode? ClassifyBind(
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax decoAccess,
        IMethodSymbol method,
        ElementTemplateNode element,
        FactoryArguments args,
        ArgumentSyntax attributeArg,
        ComposableBodyContext context)
    {
        // Roles first: the argument guard right below needs the getter's index, and BCF3017 later
        // reads that same argument. An overload this compiler was not written against therefore
        // leaves the call untranslated (BCF1003) rather than drawing surface rules about arguments
        // whose roles it has just admitted it cannot establish. The guard this replaced sat below the
        // four name diagnostics; narrowing them away here is accepted, not incidental — splitting the
        // guard to run those diagnostics first would buy nothing for a call already headed for BCF1003.
        if (!KnownSymbols.TryGetBindParameters(method, out var bind))
            return null;

        // The attribute and event names stay at their own positions. They are not delegate roles, and
        // the overload set does not move them: every Bind takes them ahead of the getter.
        if (args.At(1) is not { } eventArg || args.At(bind.GetterIndex) is not { } getterArg)
            return null;

        if (!TryGetConstantName(attributeArg.Expression, context, out var attrName))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3011, attributeArg.GetLocation(), []));
            return null;
        }

        if (!TryGetConstantName(eventArg.Expression, context, out var eventName))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3011, eventArg.GetLocation(), []));
            return null;
        }

        // Ordinal, like every other name comparison here. The prefix is never supplied for the author, so
        // its absence is the author's, and on .Bind this also catches the two adjacent string arguments
        // being written the wrong way round, which compiles.
        if (!eventName!.StartsWith("on", System.StringComparison.Ordinal))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3019, eventArg.GetLocation(), [eventName]));
            return null;
        }

        // Both names, because a binding occupies both channels: the attribute frame and the event frame
        // are as much duplicates of an .Attr and an .On as those two are of each other.
        var duplicate =
            HasBinding(element, attrName!) ? attrName!
            : HasBinding(element, eventName) ? eventName
            : null;
        if (duplicate is not null)
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3010, decoAccess.Name.GetLocation(), [duplicate]));
            return null;
        }

        // The same question for the one name the check above must let through. HasBinding cannot ask it,
        // because the folding spellings leave nothing in the channels it reads; the channel is asked
        // directly instead, and answers from its own side of BCF3024.
        if (ClassChannel.Owns(attrName) && ClassChannel.IsFolded(element))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3024, decoAccess.Name.GetLocation(), []));
            return null;
        }

        if (BindTargetResolver.TryGetBody(getterArg.Expression, out var getterBody)
            != BindTargetFailure.None)
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3017, getterArg.GetLocation(), []));
            return null;
        }

        var setter = args.At(bind.SetterIndex)?.Expression;

        // Only the inverted form needs an assignable target. With an explicit setter the getter is read
        // and never written, so a call or a get-only property is a legitimate thing to show.
        if (setter is null
            && BindTargetResolver.CheckAssignable(getterBody!, context.SemanticModel, context.CancellationToken)
                != BindTargetFailure.None)
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3018, getterBody!.GetLocation(), [getterBody!.ToString()]));
            return null;
        }

        var value = ExpressionTemplateFactory.Create(getterBody!, context);

        // The bound type and the setter's shape are read off the overload the C# compiler picked rather
        // than guessed from the syntax: the surface declares one Bind per (value type, setter shape), so
        // the resolved symbol already answers both questions exactly. That reading is the whole of this
        // layer's part in the binder — the call built around it is the emitter's (#195). The asynchrony
        // flag is forced false without a setter, where BindParameters leaves it meaningless, so that one
        // binding never has two spellings for the incremental cache to tell apart.
        return element with
        {
            Bindings = element.Bindings.AsImmutableArray().Add(new BindTemplate(
                attrName!,
                eventName,
                value,
                bind.ValueType.ToDisplayString(FullyQualifiedTypeName),
                setter is null ? null : ExpressionTemplateFactory.Create(setter, context),
                setter is not null && bind.SetterIsAsynchronous)),
        };
    }

    /// <summary>
    /// Classifies <c>.Bind(selector, get)</c> and its explicit-setter forms onto <paramref name="inner"/>,
    /// appending the two or three component parameters the binding lowers to, or reports why it cannot be.
    /// </summary>
    /// <param name="property">The parameter the selector resolved to, already checked by the shared
    /// <c>.Param</c> / <c>.Template</c> / <c>.Bind</c> prologue for BCF3005, BCF3006 and BCF3007.</param>
    /// <param name="getterArg">The getter argument, already required non-<see langword="null"/> by the same
    /// shared prologue (as <c>valueArg</c>); passed through rather than re-derived from <paramref
    /// name="args"/>.</param>
    /// <remarks>
    /// <para>
    /// Where the element surface makes the author write both names, this one derives them:
    /// <c>{name}Changed</c> always, <c>{name}Expression</c> when the component declares it. That is only
    /// admissible because <c>TComponent</c> is a known type symbol, so a derived name can be looked up
    /// and checked. The two names are then treated differently, and deliberately so:
    /// <c>{name}Changed</c> must exist and carry <c>EventCallback&lt;TValue&gt;</c> or the binding is
    /// rejected (BCF3020), while <c>{name}Expression</c> is emitted when it exists and matches and is
    /// omitted silently otherwise (see the comment at its emission below for why). The element surface
    /// has nothing to check either derivation against (its tag is a string), which is the whole of the
    /// difference.
    /// </para>
    /// <para>
    /// Rejections register a <see cref="ComposableBodyContext.RejectUnresolvedValueRecovery"/> span for the
    /// whole invocation, as the <c>.Param</c> arm and <see cref="ClassifyBind"/> do, so the failure scanner
    /// does not go on to report on the types of a getter or setter that never reaches generated code. The
    /// defensive <see langword="null"/> returns do not, for the reason <see cref="ClassifyBind"/>'s own
    /// remarks give.
    /// </para>
    /// <para>
    /// The duplicate check the shared <c>.Param</c> / <c>.Template</c> / <c>.Bind</c> prologue runs (see the
    /// caller) spans <c>{name}</c> and, via <see cref="HasBinding"/> just above, <c>{name}Changed</c> — but
    /// not <c>{name}Expression</c>. So
    /// <c>.Param(c =&gt; c.ValueExpression, …).Bind(c =&gt; c.Value, …)</c>, written in that order, does not
    /// become BCF3007: both calls append a <c>ValueExpression</c> parameter frame, and the later one silently
    /// wins. The reverse order is caught, because then it is the <c>.Param</c> arm's own duplicate check that
    /// sees the frame this method already appended. The gap is not widened, for two reasons: what a duplicate
    /// check would discard here is the author's own hand-written expression, while what wins is the derived
    /// getter lambda, which is the correct <c>{name}Expression</c> in most cases anyway; and constructing an
    /// <c>Expression&lt;Func&lt;T&gt;&gt;</c> by hand is not a shape anyone writes by accident.
    /// <c>{name}Changed</c> is checked because there, being discarded is the actual behavior, not an accident
    /// to guard against.
    /// </para>
    /// </remarks>
    private static ComponentTemplateNode? ClassifyComponentBind(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ComponentTemplateNode inner,
        IPropertySymbol property,
        ExpressionSyntax selector,
        FactoryArguments args,
        ArgumentSyntax getterArg,
        ComposableBodyContext context)
    {
        var valueType = property.Type;
        var changedName = property.Name + "Changed";

        // The derived name occupies a channel of its own, so a .Param that already wrote it is the same
        // dead duplicate as two .Param on one name — and a likelier mistake here, because the author never
        // wrote the name this collides with.
        if (HasBinding(inner, changedName))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3007, selector.GetLocation(), [changedName]));
            return null;
        }

        // TComponent comes from the constructed ComponentView<TComponent> this Bind is a member of, not
        // from the selected property's containing type: the property may be declared on a base class,
        // while the derived names must be looked up on the type the author actually wrote.
        if (method.ContainingType is not { TypeArguments.Length: 1 } componentViewType)
            return null;

        var componentType = componentViewType.TypeArguments[0];

        if (FindSettableParameter(componentType, changedName, context) is not { } changed
            || !IsChangeCallbackFor(changed, valueType, context))
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3020,
                selector.GetLocation(),
                [
                    componentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    changedName,
                    valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    property.Name,
                ]));
            return null;
        }

        if (BindTargetResolver.TryGetBody(getterArg.Expression, out var getterBody)
            != BindTargetFailure.None)
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3017, getterArg.GetLocation(), []));
            return null;
        }

        // The getter argument arrives from the shared .Param / .Template / .Bind prologue, which reads
        // argument 1 for all three: that position belongs to the three spellings sharing a selector,
        // not to the Bind overload set, and routing it through a Bind-only helper would mean branching
        // a check that exists to serve all three. Only the setter is this overload set's own.
        if (!KnownSymbols.TryGetBindParameters(method, out var bind))
            return null;

        var setter = args.At(bind.SetterIndex)?.Expression;

        // Only the inverted form needs an assignable target, exactly as on the element surface: with an
        // explicit setter the getter is read and never written.
        if (setter is null
            && BindTargetResolver.CheckAssignable(getterBody!, context.SemanticModel, context.CancellationToken)
                != BindTargetFailure.None)
        {
            context.RejectUnresolvedValueRecovery(invocation.Span);
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3018, getterBody!.GetLocation(), [getterBody!.ToString()]));
            return null;
        }

        var valueTypeName = valueType.ToDisplayString(FullyQualifiedTypeName);
        var value = ExpressionTemplateFactory.Create(getterBody!, context);

        var parameters = inner.Parameters.AsImmutableArray()
            .Add(new ComponentParameter(property.Name, value))
            .Add(new ComponentParameter(
                changedName,
                BuildChangeCallback(value, valueTypeName, setter, bind.SetterIsAsynchronous, context)));

        // Emitted only when the component declares it. This is Razor's measured behaviour: a component
        // without the parameter receives two, one with it receives three. Always emitting it would fail to
        // bind on every component that does not declare it.
        var expressionName = property.Name + "Expression";
        if (FindSettableParameter(componentType, expressionName, context) is { } fieldExpression
            && IsFieldExpressionFor(fieldExpression, valueType, context))
        {
            parameters = parameters.Add(new ComponentParameter(
                expressionName,
                BuildFieldExpression(getterArg.Expression, valueTypeName, context)));
        }

        // Value, then {name}Changed, then {name}Expression: the order Razor emits, and the order the
        // sequence numbers have to follow, since they stand for source positions in one expression.
        return new ComponentTemplateNode(inner.TypeName, parameters, inner.Slots);
    }

    /// <summary>
    /// Builds the <c>EventCallback.Factory.Create&lt;T&gt;(this, …)</c> expression a component binding's
    /// <c>{name}Changed</c> parameter carries, around the getter's own segments.
    /// </summary>
    /// <remarks>
    /// Composed from segments and never from <see cref="ExpressionTemplate.Literal"/> over
    /// <c>ToCode()</c>: inside a <c>[Composable]</c> body the getter still holds unbound parameter holes,
    /// and <c>ToCode()</c> throws on those, so the holes are carried through for
    /// <c>ComposableExpander</c> to substitute.
    /// <para>
    /// The cast around the setter is required for the same reason the element side needs one: a lambda
    /// written in an argument position has no natural type, and <c>Create</c>'s own overloads cannot pick
    /// one for it once it travels through this template.
    /// </para>
    /// </remarks>
    private static ExpressionTemplate BuildChangeCallback(
        ExpressionTemplate value,
        string valueTypeName,
        ExpressionSyntax? setter,          // null = invert the getter
        bool setterIsAsynchronous,
        ComposableBodyContext context)
    {
        var segments = ImmutableArray.CreateBuilder<ExpressionSegment>();
        segments.Add(new LiteralExpressionSegment($"{CreateCall}{valueTypeName}>(this, "));

        if (setter is null)
        {
            // Create<T>(this, (Action<T>)(__value => <value> = __value))
            segments.Add(new LiteralExpressionSegment(
                $"(global::System.Action<{valueTypeName}>)(__value => "));
            segments.AddRange(value.Segments.AsImmutableArray());
            segments.Add(new LiteralExpressionSegment(" = __value)"));
        }
        else
        {
            // Create<T>(this, (Action<T>)(<setter>)) or, for an asynchronous setter,
            // Create<T>(this, (Func<T, Task>)(<setter>)).
            var setterType = setterIsAsynchronous
                ? $"global::System.Func<{valueTypeName}, global::System.Threading.Tasks.Task>"
                : $"global::System.Action<{valueTypeName}>";
            segments.Add(new LiteralExpressionSegment($"({setterType})("));
            segments.AddRange(ExpressionTemplateFactory.Create(setter, context).Segments.AsImmutableArray());
            segments.Add(new LiteralExpressionSegment(")"));
        }

        segments.Add(new LiteralExpressionSegment(")"));
        return ExpressionTemplate.Create(segments.ToImmutable());
    }

    /// <summary>
    /// Builds the <c>{name}Expression</c> parameter's value: the getter lambda itself, whole, cast to the
    /// expression-tree type the parameter declares. This is what identifies the bound field to an
    /// <c>EditForm</c>, and no other spelling of the target could supply it.
    /// </summary>
    private static ExpressionTemplate BuildFieldExpression(
        ExpressionSyntax getter, string valueTypeName, ComposableBodyContext context)
    {
        ImmutableArray<ExpressionSegment> segments =
        [
            new LiteralExpressionSegment(
                "(global::System.Linq.Expressions.Expression<global::System.Func<"
                + valueTypeName + ">>)("),
            .. ExpressionTemplateFactory.Create(getter, context).Segments.AsImmutableArray(),
            new LiteralExpressionSegment(")"),
        ];

        return ExpressionTemplate.Create(segments);
    }

    /// <summary>
    /// The settable <c>[Parameter]</c> named <paramref name="name"/> that <paramref name="componentType"/>
    /// declares or inherits, or <see langword="null"/>. Walks the base chain for the reason
    /// <see cref="HasUsableChildContent"/> does: Blazor accepts a parameter declared on a base class, and
    /// Roslyn's <c>GetMembers</c> on the derived type alone would not see it.
    /// </summary>
    private static IPropertySymbol? FindSettableParameter(
        ITypeSymbol componentType, string name, ComposableBodyContext context)
    {
        for (var current = componentType; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol property && IsSettableParameter(property, context))
                    return property;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="changed"/> can receive a binding's write-back for
    /// <paramref name="valueType"/>: its type is exactly <c>EventCallback&lt;TValue&gt;</c>.
    /// </summary>
    private static bool IsChangeCallbackFor(
        IPropertySymbol changed, ITypeSymbol valueType, ComposableBodyContext context) =>
        context.KnownSymbols.EventCallbackType is { } eventCallbackType
        && changed.Type is INamedTypeSymbol { TypeArguments.Length: 1 } callback
        && SymbolEqualityComparer.Default.Equals(callback.OriginalDefinition, eventCallbackType)
        && SymbolEqualityComparer.Default.Equals(callback.TypeArguments[0], valueType);

    /// <summary>
    /// Whether <paramref name="fieldExpression"/> is a <c>{name}Expression</c> parameter for
    /// <paramref name="valueType"/>: its type is exactly
    /// <c>Expression&lt;Func&lt;TValue&gt;&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Nullable annotations play no part: <see cref="SymbolEqualityComparer.Default"/> ignores them, and
    /// the parameter is conventionally declared <c>Expression&lt;Func&lt;TValue&gt;&gt;?</c> while the
    /// value assigned to it never is.
    /// </remarks>
    private static bool IsFieldExpressionFor(
        IPropertySymbol fieldExpression, ITypeSymbol valueType, ComposableBodyContext context)
    {
        var symbols = context.KnownSymbols;
        if (symbols.ExpressionType is not { } expressionType || symbols.FuncType is not { } funcType)
            return false;

        return fieldExpression.Type is INamedTypeSymbol { TypeArguments.Length: 1 } tree
            && SymbolEqualityComparer.Default.Equals(tree.OriginalDefinition, expressionType)
            && tree.TypeArguments[0] is INamedTypeSymbol { TypeArguments.Length: 1 } getter
            && SymbolEqualityComparer.Default.Equals(getter.OriginalDefinition, funcType)
            && SymbolEqualityComparer.Default.Equals(getter.TypeArguments[0], valueType);
    }

    /// <summary>
    /// Classifies an element access whose resolved symbol is an indexer, children written in brackets,
    /// returning <see langword="null"/> when the indexer is not one of the design-time surface's.
    /// </summary>
    /// <remarks>
    /// Every comparison is guarded on the known symbol being present rather than made directly.
    /// <c>ElementIndexer</c> and <c>ComponentIndexer</c> resolve to <see langword="null"/> against a runtime
    /// without the bracket surface, and <c>SymbolEqualityComparer.Default.Equals(x, null)</c> answers
    /// <see langword="true"/> for a null <c>x</c>, so an unguarded comparison would classify any unrelated
    /// indexer, <c>_dict["k"]</c>, as an element.
    /// </remarks>
    private static RenderTemplateNode? ClassifyIndexer(
        ElementAccessExpressionSyntax elementAccess,
        IPropertySymbol indexer,
        ComposableBodyContext context)
    {
        var symbols = context.KnownSymbols;
        var definition = indexer.OriginalDefinition;

        if (symbols.ElementIndexer is { } elementIndexer
            && SymbolEqualityComparer.Default.Equals(definition, elementIndexer))
        {
            return ClassifyElementIndexer(elementAccess, context);
        }

        if (symbols.ComponentIndexer is { } componentIndexer
            && SymbolEqualityComparer.Default.Equals(definition, componentIndexer))
        {
            return ClassifyComponentIndexer(elementAccess, indexer, context);
        }

        if (symbols.ContentIndexer is { } contentIndexer
            && SymbolEqualityComparer.Default.Equals(definition, contentIndexer))
        {
            return ClassifyComposableContentIndexer(elementAccess, context);
        }

        return null;
    }

    /// <summary>
    /// Classifies <c>Card("Profile")[P["本文"]]</c>: the call and its value arguments come from the element
    /// access's own receiver, the content from its bracketed arguments (#176).
    /// </summary>
    /// <remarks>
    /// The receiver is classified by the same arm that handles a bare call, so nothing about argument binding
    /// is written twice. The bracket content becomes one more <see cref="ComposableContentArgument"/>, at the
    /// ordinal after the callee's last parameter, which is where the definition binds its slot: the receiver's
    /// own symbol gives the arity, so the call site names the ordinal rather than leaving two transports for
    /// the expander to reconcile.
    /// <para>
    /// Several children are grouped in a <see cref="FragmentTemplateNode"/> and a single one is kept as
    /// itself, the rule <see cref="TryBuildChildContentSlot"/> already applies to the analogous case, so
    /// wrapping a part around one child emits exactly the frames writing that child inline would.
    /// </para>
    /// <para>
    /// There is no "brackets on a part that takes no content" case to diagnose. Such a part returns
    /// <c>View</c>, which declares no indexer, so the bracket is a CS0021 before the generator runs — the same
    /// property that makes <c>Div["text"].Class("card")</c> a CS1929 rather than a second supported style.
    /// </para>
    /// </remarks>
    private static ComposableCallTemplateNode? ClassifyComposableContentIndexer(
        ElementAccessExpressionSyntax elementAccess, ComposableBodyContext context)
    {
        if (Analyze(elementAccess.Expression, context) is not ComposableCallTemplateNode call)
            return null;

        // The callee's arity, which is the slot's ordinal. Taken from the receiver's own resolved symbol; the
        // template node is symbol-free and cannot carry it.
        if (context.SemanticModel.GetSymbolInfo(elementAccess.Expression, context.CancellationToken).Symbol
            is not IMethodSymbol callee)
        {
            return null;
        }

        var children = TryAnalyzeBracketChildren(elementAccess, context);
        if (children is null)
            return null;

        RenderTemplateNode content = children.Value.Length == 1
            ? children.Value[0]
            : new FragmentTemplateNode(children.Value);

        return call with
        {
            ContentArguments = call.ContentArguments.AsImmutableArray()
                .Add(new ComposableContentArgument(callee.Parameters.Length, content)),
        };
    }

    /// <summary>
    /// Classifies <c>Div[…]</c> and <c>Div.Class("card")[…]</c>: the tag and any decorations come from the
    /// element access's own receiver, the children from its bracketed arguments. This is also the one place
    /// BCF3016 can be reported, because it is where a resolved tag and a child list meet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BCF3016 is anchored on the whole element access, matching BCF3013 rather than the argument list:
    /// both diagnostics are about a tag and a child list being written together, and either half can be
    /// the one to change (<c>Img["logo"]</c> is fixed by <c>Img.Alt("logo")</c> as readily as by dropping
    /// the brackets), so the report does not presuppose which.
    /// </para>
    /// <para>
    /// No failure path here registers a <see cref="ComposableBodyContext.RejectUnresolvedValueRecovery"/>
    /// span, and none usefully could: that suppressor is matched as an exact <c>TextSpan</c>, and its only
    /// reader, <c>UnresolvedValueTypeScanner</c>, always looks up an <c>InvocationExpressionSyntax</c>
    /// span, so a rejection keyed on this element access could never be read. Where suppression is
    /// genuinely needed it is registered by a receiver that is an invocation: the decoration and
    /// <c>.Param</c> arms reject their own spans. The construct that looks like it needs one here does
    /// not: <c>Element(nonConstant)["x"]</c> reports BCF3009 and no BCF3015, because the scanner's
    /// <c>Element</c> arm never reports on the tag argument at all, and its own constant-tag gate keeps it
    /// out of the children of an element BCF3009 has already rejected. BCF3016's own rejection is the same
    /// case: the element access is not an invocation, so there is nothing the scanner could match.
    /// </para>
    /// </remarks>
    private static ElementTemplateNode? ClassifyElementIndexer(
        ElementAccessExpressionSyntax elementAccess, ComposableBodyContext context)
    {
        // The receiver carries the tag and the decoration chain, so it is classified by the same arms that
        // handle the childless and decorated forms rather than by a second copy of their rules.
        if (Analyze(elementAccess.Expression, context) is not ElementTemplateNode element)
            return null;

        var children = TryAnalyzeBracketChildren(elementAccess, context);
        if (children is null)
            return null;

        // Checked after the children are classified rather than before, as BCF3013 is: a child that cannot
        // be translated at all is the more basic complaint, and reporting it keeps BCF1003 pointing at the
        // innermost failure instead of blaming the brackets. What counts as children is any argument at
        // all, so Img[Fragment()] and Img[If(false, …)] report too, even though they happen to emit a
        // correct <img />. Neither has a reason to be written, and admitting them would make the rule
        // depend on what a child evaluates to rather than on the element's tag.
        if (children.Value.Length > 0 && KnownSymbols.IsVoidTag(element.Tag))
        {
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3016,
                elementAccess.GetLocation(),
                [element.Tag]));
            return null;
        }

        return element with { Children = children.Value };
    }

    /// <summary>
    /// Classifies <c>Component&lt;T&gt;()[…]</c> and <c>Component&lt;T&gt;().Param(…)[…]</c>: the target type
    /// and any parameters come from the element access's own receiver, the child content from its bracketed
    /// arguments.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The duplicate check here is not a mirror of the one in the <c>.Param</c> arm but the other half of it.
    /// The indexer returns <c>View</c>, so nothing can follow the brackets: children are always written last,
    /// and a <c>.Param</c> on <c>ChildContent</c> is therefore always the binding that came first. The
    /// <c>.Param</c> arm only ever sees a receiver written before it, so it cannot see these children; this
    /// arm has to perform the check or BCF3007 goes silent for the whole combination.
    /// </para>
    /// <para>
    /// The same property fixes the slot order: children cannot be followed by anything, so
    /// <c>ChildContent</c> is invariably the last slot and appending is the only order this surface can
    /// produce. It is also the correct one: sequence numbers represent source syntax positions, so a slot
    /// written last is numbered last. <c>BracketSurfaceSlotOrderTests</c> pins it, because no corpus baseline
    /// covers a fragment <c>.Param</c> beside children.
    /// </para>
    /// </remarks>
    private static ComponentTemplateNode? ClassifyComponentIndexer(
        ElementAccessExpressionSyntax elementAccess,
        IPropertySymbol indexer,
        ComposableBodyContext context)
    {
        if (Analyze(elementAccess.Expression, context) is not ComponentTemplateNode component)
            return null;

        // The node carries only the type's display name, so the symbol BCF3013 and the ChildContent lookup
        // need comes from the indexer's own containing type, ComponentView<T> for the T being configured.
        if (indexer.ContainingType is not { TypeArguments.Length: 1 } componentViewType)
            return null;

        var children = TryAnalyzeBracketChildren(elementAccess, context);
        if (children is null)
            return null;

        if (children.Value.Length > 0 && HasBinding(component, ChildContentParameterName))
        {
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3007,
                elementAccess.ArgumentList.GetLocation(),
                [ChildContentParameterName]));
            return null;
        }

        if (!TryBuildChildContentSlot(
                children.Value,
                componentViewType.TypeArguments[0],
                elementAccess.GetLocation(),
                context,
                out var slots))
            return null;

        // Appended, not assigned: a .Param on another fragment parameter (c => c.Footer) has already put a
        // slot on the receiver, and it is not a duplicate of this one. Appending is also the only order
        // available, see the remarks on slot order.
        var appended = component.Slots.AsImmutableArray().AddRange(slots.AsImmutableArray());
        return new ComponentTemplateNode(component.TypeName, component.Parameters, appended);
    }

    /// <summary>
    /// Builds the single <c>ChildContent</c> slot children are bound to, reporting BCF3013 at
    /// <paramref name="location"/> when <paramref name="componentType"/> cannot receive them. Yields an
    /// empty slot list, and succeeds, when there are no children.
    /// </summary>
    /// <remarks>
    /// Called only from <see cref="ClassifyComponentIndexer"/>: <c>ComponentView&lt;T&gt;</c>'s indexer is the
    /// one channel children reach a component through. Kept as its own method rather than inlined because
    /// the BCF3013 rule, which components can receive children, and where the report lands, is worth
    /// naming separately from the indexer's argument handling.
    /// </remarks>
    private static bool TryBuildChildContentSlot(
        ImmutableArray<RenderTemplateNode> children,
        ITypeSymbol componentType,
        Location location,
        ComposableBodyContext context,
        out EquatableArray<ComponentSlot> slots)
    {
        slots = EquatableArray<ComponentSlot>.Empty;
        if (children.Length == 0)
            return true;

        if (!HasUsableChildContent(componentType, context))
        {
            context.Diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticDescriptors.BCF3013,
                location,
                [componentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]));
            return false;
        }

        // All children share the single ChildContent fragment; a Fragment node groups them without emitting
        // a wrapper element, matching how Razor lowers multiple nested children.
        RenderTemplateNode content = children.Length == 1
            ? children[0]
            : new FragmentTemplateNode(children);

        // ImmutableArray.Create, not a collection expression: the target type is EquatableArray<ComponentSlot>
        // so IDE0303 does not apply, and spelling it [x] does not compile.
        slots = ImmutableArray.Create(new ComponentSlot(ChildContentParameterName, content));
        return true;
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

    /// <summary>
    /// Whether <paramref name="node"/> already binds <paramref name="name"/> in either channel. An
    /// element's attributes and events share one name space once emitted, both become
    /// <c>AddAttribute</c> frames, and Blazor resolves each name to a single value no matter which
    /// channel produced it, so <c>.Attr("onclick", …)</c> next to <c>.OnClick(…)</c> is the same dead
    /// duplicate as two <c>.OnClick</c>. The folding spellings of <c>class</c> never reach this check —
    /// both <c>.Class</c> and <c>.Attr("class", …)</c> route to <see cref="ElementTemplateNode.Classes"/>
    /// first, which is how the one repeatable attribute stays legal — so this is never asked about that
    /// name at all. What collides there is the channel rather than the name, and
    /// <see cref="ClassChannel"/> answers it from both sides as BCF3024.
    /// <para>
    /// A binding occupies both names it was given, and both are checked here rather than only in the
    /// bind arm, so that the answer does not depend on decoration order:
    /// <c>.Bind("value", …).Attr("value", …)</c> and the reverse spelling are the same dead duplicate and
    /// report alike.
    /// </para>
    /// </summary>
    private static bool HasBinding(ElementTemplateNode node, string name)
    {
        foreach (var attribute in node.Attributes.AsImmutableArray())
        {
            if (string.Equals(attribute.Name, name, System.StringComparison.Ordinal))
                return true;
        }

        foreach (var @event in node.Events.AsImmutableArray())
        {
            if (string.Equals(@event.Name, name, System.StringComparison.Ordinal))
                return true;
        }

        foreach (var bind in node.Bindings)
        {
            if (string.Equals(bind.AttributeName, name, System.StringComparison.Ordinal)
                || string.Equals(bind.EventName, name, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Analyzes each child expression into a child template node, returning null if any child
    /// cannot be statically analyzed (propagated as translation failure).</summary>
    /// <summary>
    /// The children written in an element access's brackets, or <see langword="null"/> when they cannot be
    /// analyzed. The one place the bracket channel's binding rule lives, shared by all three indexers that
    /// carry it — the element's, <c>ComponentView&lt;T&gt;</c>'s, and <c>SlotView</c>'s.
    /// </summary>
    /// <remarks>
    /// One whole collection passed to the params indexer (<c>Div[arr]</c>) is not a list of children, so it is
    /// left unanalyzable and lands on BCF1003 rather than being mis-split. That rule held in three copies
    /// before this was extracted, where it could be changed in one arm and missed in the others.
    /// </remarks>
    private static ImmutableArray<RenderTemplateNode>? TryAnalyzeBracketChildren(
        ElementAccessExpressionSyntax elementAccess, ComposableBodyContext context)
    {
        if (FactoryArguments.Bind(elementAccess, context) is not { } args || args.HasUnanalyzableParamsArgument)
            return null;

        return AnalyzeChildren(args.ParamsElements, context);
    }

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
        ExpressionSyntax expression, ComposableBodyContext context, [MaybeNullWhen(false)] out string name)
    {
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant is { HasValue: true, Value: string value } && !string.IsNullOrWhiteSpace(value))
        {
            name = value;
            return true;
        }
        name = null!;
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

    /// <param name="contentArguments">
    /// The call's <c>View</c>-typed arguments, its additional content slots (#34), classified as node subtrees
    /// rather than as expressions. Empty for a call that has none, which is every call to a part declared
    /// before this surface existed.
    /// </param>
    private static ImmutableArray<ComposableInvocationArgument>? CreateInvocationArguments(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        ComposableBodyContext context,
        out ImmutableArray<ComposableContentArgument> contentArguments)
    {
        contentArguments = [];
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken)
            is not IInvocationOperation operation)
        {
            return null;
        }

        // Explicitly supplied arguments sort by their source position; implicit/default arguments sort
        // after every supplied argument. Operation arguments are parameter-ordered, so the enumeration
        // index cannot be used as source order.
        var builder = ImmutableArray.CreateBuilder<ComposableInvocationArgument>(operation.Arguments.Length);
        var contentBuilder = ImmutableArray.CreateBuilder<ComposableContentArgument>();
        foreach (var argument in operation.Arguments)
        {
            var parameter = argument.Parameter;
            if (parameter is null)
                return null;

            var isImplicitDefault = argument.ArgumentKind == ArgumentKind.DefaultValue;

            // Ordinarily argument.Syntax IS the ArgumentSyntax. But when the argument expression is a bare
            // null-forgiving suppression with nothing else to convert (e.g. `Target(value!)`), Roslyn elides
            // the suppression operator from the operation tree and Syntax points at the innermost operand
            // instead, so look for the enclosing ArgumentSyntax rather than requiring an exact cast. Mirrors
            // FactoryArguments.Bind's default arm, which the design-time syntax path already uses for the same
            // elision.
            //
            // The walk is confined to this call's own list, which is what makes it safe for every ArgumentKind
            // rather than only for the ones some other file keeps away. An argument Roslyn synthesizes carries
            // the whole invocation as its Syntax -- the receiver of a reduced extension call (#203) and a params
            // argument both do -- and that sits under no ArgumentSyntax of this call, so an unconfined walk
            // climbs to an *enclosing* call's argument and binds an unrelated expression to the parameter.
            // FactoryArguments.Bind keeps the same walk safe by diverting each such kind ahead of it; confining
            // the walk answers for all of them at once, so no caller stays disciplined on this loop's behalf.
            //
            // Resolved once here rather than per arm below, so the confinement rule is stated in one place.
            ArgumentSyntax? written = null;
            if (!isImplicitDefault)
            {
                written = argument.Syntax.FirstAncestorOrSelf<ArgumentSyntax>();
                if (written is null || written.Parent != invocation.ArgumentList)
                    return null;
            }

            // A View-typed parameter is a content slot, so its argument is classified as a node subtree and
            // routed to the content channel rather than lowered to a local (#34). An omitted one has no
            // subtree to route: the callee's own declaration forbids an optional View parameter, so this is
            // only reachable while that declaration is itself invalid, and leaving the call unanalyzable lets
            // the declaration's BCF1002 be the report.
            if (context.KnownSymbols.IsContentType(parameter.Type))
            {
                if (written is null)
                    return null;

                var content = Analyze(written.Expression, context);
                if (content is null)
                    return null;

                contentBuilder.Add(new ComposableContentArgument(parameter.Ordinal, content));
                continue;
            }

            ExpressionTemplate value;
            int sourceOrder;
            if (isImplicitDefault)
            {
                // The only argument that needs the parameter's type name: the default's cast is spelled
                // from it. A supplied argument carries the author's own syntax and never asks.
                value = ConstantTemplate.ForParameterDefault(
                    parameter,
                    parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

                // Strictly increasing in parameter ordinal and always greater than any source
                // position (a small non-negative span start), so implicit defaults sort after every
                // supplied argument while staying in parameter order. Subtracting the parameter count
                // before adding the ordinal keeps the value below int.MaxValue and cannot overflow,
                // unlike a formula that could add to int.MaxValue when trailing optionals are omitted.
                sourceOrder = int.MaxValue - method.Parameters.Length + parameter.Ordinal;
            }
            else
            {
                value = ExpressionTemplateFactory.Create(written!.Expression, context);
                sourceOrder = argument.Syntax.SpanStart;
            }

            builder.Add(new ComposableInvocationArgument(
                parameter.Ordinal,
                sourceOrder,
                isImplicitDefault,
                value));
        }

        contentArguments = contentBuilder.ToImmutable();
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
    /// Resolves the name a decoration targets, reporting BCF3011 when a non-shortcut spelling names it with
    /// something that is not a constant.
    /// </summary>
    /// <param name="shortcutName">
    /// The name a named shortcut implies (<c>.Href</c> → <c>href</c>, <c>.OnClick</c> → <c>onclick</c>),
    /// or <see langword="null"/> for the generic <c>.Attr</c>/<c>.On</c> spellings that take the name as
    /// their first argument. Non-null exactly when the resolved method was found in
    /// <see cref="KnownSymbols.AttributeShortcuts"/> or <see cref="KnownSymbols.EventShortcuts"/>, whose
    /// values are never null.
    /// </param>
    /// <remarks>
    /// The attribute channel and the event channel ask the same question here and must answer it the same
    /// way: the two ladders this replaces were an eighteen-line transcription of each other, so a change to
    /// how a non-constant name is diagnosed had to be made twice or the two would disagree about the same
    /// mistake. What genuinely differs between them stays at the call sites, and the value is one of those
    /// differences — not merely where it sits, but what its absence means. The attribute channel's
    /// <see langword="bool"/> value is an optional whose default is <see langword="true"/> (#178), so an
    /// omitted argument there is a spelling; no event overload has an optional handler, so an omitted one
    /// there is a call this compiler was not written against and must reach BCF1003. Synthesizing a value
    /// here would emit <c>AddAttribute(seq, "onclick", true)</c> for the second case.
    /// </remarks>
    private static bool TryResolveDecorationName(
        InvocationExpressionSyntax invocation,
        ArgumentSyntax firstArg,
        string? shortcutName,
        ComposableBodyContext context,
        [MaybeNullWhen(false)] out string name)
    {
        if (shortcutName is not null)
        {
            name = shortcutName;
            return true;
        }

        if (TryGetConstantName(firstArg.Expression, context, out name))
            return true;

        context.RejectUnresolvedValueRecovery(invocation.Span);
        context.Diagnostics.Add(DiagnosticInfo.Create(
            DiagnosticDescriptors.BCF3011, firstArg.GetLocation(), []));
        return false;
    }

    /// <summary>
    /// Reads the context type of a <c>RenderFragment&lt;TContext&gt;</c>-typed slot property, failing when the
    /// selected property is not one.
    /// </summary>
    /// <remarks>
    /// Both <c>.Template</c> arms ask this, and the answer must be the same for both: they differ in whether
    /// the author names the context, not in what the context is. The check is not redundant with
    /// <see cref="KnownSymbols.ClassifySurfaceMethod"/>, which proves the <em>method</em> is a
    /// <c>Template</c> overload; this proves the <em>selected property</em> is generic, and a selector may
    /// name any property on the component.
    /// </remarks>
    private static bool TryGetFragmentContextTypeName(
        IPropertySymbol property, KnownSymbols symbols, out string contextTypeName)
    {
        if (property.Type is not INamedTypeSymbol { TypeArguments.Length: 1 } genericFragment
            || symbols.RenderFragmentGenericType is not { } renderFragmentGenericType
            || !SymbolEqualityComparer.Default.Equals(
                genericFragment.OriginalDefinition, renderFragmentGenericType))
        {
            contextTypeName = string.Empty;
            return false;
        }

        contextTypeName = genericFragment.TypeArguments[0].ToDisplayString(FullyQualifiedTypeName);
        return true;
    }

    /// <summary>
    /// Returns <paramref name="inner"/> with one more slot appended, preserving source order. The three slot
    /// arms differ only in the kind and context type they pass here.
    /// </summary>
    private static ComponentTemplateNode AppendSlot(
        ComponentTemplateNode inner,
        string name,
        RenderTemplateNode content,
        ComponentSlotKind kind = ComponentSlotKind.NonGeneric,
        string? contextTypeName = null) =>
        new(
            inner.TypeName,
            inner.Parameters,
            inner.Slots.AsImmutableArray().Add(
                new ComponentSlot(name, content) { Kind = kind, ContextTypeName = contextTypeName }));

    /// <summary>
    /// Succeeds only when <paramref name="selector"/> is <c>p =&gt; p.Property</c>, a member access whose
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
    /// <c>RenderFragment</c>. A <c>RenderFragment&lt;TContext&gt;</c> is excluded deliberately, the
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
    /// Whether <paramref name="type"/> is one of the inert design-time markers (<c>View</c>,
    /// <c>ElementView</c>, or a <c>ComponentView&lt;T&gt;</c> construction). The generic Param emits its
    /// value verbatim, so such a value would bind the empty marker instead of content.
    /// </summary>
    private static bool IsInertDesignTimeType(ITypeSymbol? type, ComposableBodyContext context)
    {
        if (type is null)
            return false;

        var symbols = context.KnownSymbols;

        if (symbols.ViewType is { } viewType && SymbolEqualityComparer.Default.Equals(type, viewType))
            return true;

        // A childless element is an ElementView rather than a View, so without this arm
        // .Param(c => c.Payload, Div) passes through and emits `Div` verbatim.
        if (symbols.ElementViewType is { } elementViewType
            && SymbolEqualityComparer.Default.Equals(type, elementViewType))
        {
            return true;
        }

        // A content-taking part's call is a SlotView before its brackets, so .Param(c => c.Payload, Card("t"))
        // type-checks through object and would otherwise emit `Card("t")` verbatim, exactly as the
        // ElementView arm above exists to prevent for `Div`.
        if (symbols.SlotViewType is { } slotViewType
            && SymbolEqualityComparer.Default.Equals(type, slotViewType))
        {
            return true;
        }

        return symbols.ComponentViewType is { } componentViewType
            && type is INamedTypeSymbol named
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, componentViewType);
    }
}

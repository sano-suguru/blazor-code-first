using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Binds an invocation's arguments to the callee's declared parameters, so the analyzer asks for "the
/// argument bound to declared parameter N" instead of "the argument at syntactic position N".  Named
/// arguments written out of declaration order therefore bind correctly.
/// </summary>
/// <remarks>
/// <para>
/// The binding is Roslyn's, not ours: <see cref="IArgumentOperation.Parameter"/> already carries the
/// resolved parameter, so no argument-position rule is reimplemented here.  This is the same mechanism
/// <c>RenderExpressionAnalyzer.CreateInvocationArguments</c> already uses for <c>[Composable]</c> calls.
/// </para>
/// <para>
/// It also removes the reduced/unreduced extension-method hazard.
/// <see cref="IInvocationOperation.TargetMethod"/> is the <em>unreduced</em> symbol whose parameter 0 is
/// the receiver — in both the fluent (<c>view.Attr(...)</c>) and static (<c>Decorations.Attr(view, ...)</c>)
/// call forms — so the receiver offset is applied in exactly one place instead of being a trap at every
/// call site.  Do not pass a <c>KnownSymbols.Normalize</c> result anywhere near this: that key exists for
/// map lookups only.
/// </para>
/// <para>
/// Every failure yields <see langword="null"/> or an empty result rather than an exception: this runs
/// over incomplete code in the IDE, and an exception inside a generator surfaces as a build error.
/// </para>
/// </remarks>
internal readonly struct FactoryArguments
{
    private readonly ImmutableArray<ArgumentSyntax?> _byDeclaredParameter;

    private FactoryArguments(
        ImmutableArray<ArgumentSyntax?> byDeclaredParameter,
        ImmutableArray<ExpressionSyntax> paramsElements,
        bool hasUnanalyzableParamsArgument)
    {
        _byDeclaredParameter = byDeclaredParameter;
        ParamsElements = paramsElements;
        HasUnanalyzableParamsArgument = hasUnanalyzableParamsArgument;
    }

    /// <summary>
    /// The children written into a <c>params</c> parameter, in source order — from the expanded form
    /// (<c>Div["a", "b"]</c>) or from a collection-expression literal (<c>Div[["a", "b"]]</c>), which are
    /// the same call.  Empty when the callee has no <c>params</c> parameter or it received no elements.
    /// </summary>
    internal ImmutableArray<ExpressionSyntax> ParamsElements { get; }

    /// <summary>
    /// True when the <c>params</c> parameter received one whole collection whose children are not visible
    /// to the generator — a variable or method result (<c>Div[_kids]</c>, <c>Div[children: Kids()]</c>), a
    /// written array creation, or anything containing a spread.  Callers must reject such an argument
    /// rather than mis-split it.  A collection-expression literal (<c>Div[["a", "b"]]</c>) is <em>not</em>
    /// such an argument: its children are recovered into <see cref="ParamsElements"/> like an expanded
    /// bucket's, because it is the same call (#75).
    /// </summary>
    internal bool HasUnanalyzableParamsArgument { get; }

    /// <summary>
    /// The argument bound to the declared parameter at <paramref name="index"/>, ignoring an extension
    /// method's receiver, or <see langword="null"/> when that parameter received no argument (an omitted
    /// optional) or when <paramref name="index"/> is out of range.
    /// </summary>
    internal ArgumentSyntax? At(int index) =>
        !_byDeclaredParameter.IsDefaultOrEmpty && (uint)index < (uint)_byDeclaredParameter.Length
            ? _byDeclaredParameter[index]
            : null;

    /// <summary>
    /// Binds <paramref name="invocation"/>'s arguments, or returns <see langword="null"/> when the
    /// invocation has no operation or an argument cannot be attributed to a parameter.
    /// </summary>
    internal static FactoryArguments? Bind(
        InvocationExpressionSyntax invocation, ComposableBodyContext context)
    {
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken)
            is not IInvocationOperation operation)
        {
            return null;
        }

        var method = operation.TargetMethod;

        // TargetMethod is unreduced even for a fluent extension-method call, so parameter 0 is the
        // receiver. Skip it so callers index the parameters they actually wrote.
        var offset = method.IsExtensionMethod ? 1 : 0;

        return Bind(operation.Arguments, method.Parameters.Length, offset);
    }

    /// <summary>
    /// Binds <paramref name="elementAccess"/>'s bracketed arguments, or returns <see langword="null"/> when
    /// it has no property-reference operation or an argument cannot be attributed to a parameter.
    /// </summary>
    /// <remarks>
    /// A <c>params ReadOnlySpan&lt;View&gt;</c> indexer is modelled exactly like a <c>params</c> method
    /// parameter — <see cref="ArgumentKind.ParamCollection"/> carrying an
    /// <see cref="ICollectionExpressionOperation"/> — and a <see cref="BracketedArgumentListSyntax"/>
    /// contains <see cref="ArgumentSyntax"/> nodes, so the shared loop and its null-forgiving recovery both
    /// apply unchanged.  An indexer can never be an extension method, so the receiver offset is always 0.
    /// </remarks>
    internal static FactoryArguments? Bind(
        ElementAccessExpressionSyntax elementAccess, ComposableBodyContext context)
    {
        if (context.SemanticModel.GetOperation(elementAccess, context.CancellationToken)
            is not IPropertyReferenceOperation operation)
        {
            return null;
        }

        return Bind(operation.Arguments, operation.Property.Parameters.Length, offset: 0);
    }

    /// <summary>
    /// The binding itself, shared by the invocation and element-access overloads: an argument is attributed
    /// to the declared parameter its <see cref="IArgumentOperation.Parameter"/> names, less
    /// <paramref name="offset"/> for an extension method's receiver.
    /// </summary>
    private static FactoryArguments? Bind(
        ImmutableArray<IArgumentOperation> arguments, int parameterCount, int offset)
    {
        var declaredCount = parameterCount - offset;
        if (declaredCount < 0)
            return null;

        var byParameter = new ArgumentSyntax?[declaredCount];
        var paramsElements = ImmutableArray<ExpressionSyntax>.Empty;
        var hasUnanalyzableParams = false;

        foreach (var argument in arguments)
        {
            if (argument.Parameter is not { } parameter)
                return null;

            var index = parameter.Ordinal - offset;
            if (index < 0)
                continue;                                  // the extension receiver

            // The FirstAncestorOrSelf<ArgumentSyntax>() walk in the default arm below is only safe
            // because DefaultValue continues here first, ParamCollection/ParamArray divert to
            // ExtractParamsElements before the walk, and an Explicit argument bound to a params
            // parameter diverts to TryExtractLiteralElements — so only a non-params Explicit ever
            // reaches the walk, and such an argument's Syntax is always nested inside its own call's
            // ArgumentSyntax. If a future edit let DefaultValue (whose Syntax is the invocation node
            // itself) fall through, the walk would climb to an *enclosing* call's argument and silently
            // bind an unrelated expression. Keep those three diverting first.
            switch (argument.ArgumentKind)
            {
                case ArgumentKind.DefaultValue:
                    continue;                              // omitted optional: leave the slot null

                case ArgumentKind.ParamCollection:
                case ArgumentKind.ParamArray:
                    if (ExtractParamsElements(argument) is not { } elements)
                        return null;

                    paramsElements = elements;
                    continue;

                default:
                    if (parameter.IsParams)
                    {
                        // A collection-expression literal written in the non-expanded position
                        // (Div[["a", "b"]]) is the same call as the expanded Div["a", "b"] — the
                        // params-collections rules pass the same span — and its children are present in
                        // the operation tree, so recover them rather than refuse (#75). Every other
                        // explicit shape (a variable, a method result, a written array creation, a
                        // spread) has no per-child written expression and stays unanalyzable.
                        if (TryExtractLiteralElements(argument) is { } literalElements)
                            paramsElements = literalElements;
                        else
                            hasUnanalyzableParams = true;

                        continue;
                    }

                    // Ordinarily argument.Syntax IS the ArgumentSyntax. But when the argument
                    // expression is a bare null-forgiving suppression with nothing else to convert
                    // (e.g. `Class(NullClass!)`), Roslyn elides the suppression operator from the
                    // operation tree and Syntax points at the innermost operand instead — so look for
                    // the enclosing ArgumentSyntax rather than requiring an exact match.
                    if (argument.Syntax.FirstAncestorOrSelf<ArgumentSyntax>() is not { } argumentSyntax)
                        return null;

                    byParameter[index] = argumentSyntax;
                    continue;
            }
        }

        return new FactoryArguments(
            ImmutableArray.Create(byParameter), paramsElements, hasUnanalyzableParams);
    }

    /// <summary>
    /// Unwraps the synthesized collection an expanded <c>params</c> bucket is modelled as, returning each
    /// child's own written expression, or <see langword="null"/> when the shape is unrecognized or an
    /// element's written expression cannot be recovered.  A <c>params ReadOnlySpan&lt;View&gt;</c> is a
    /// collection expression; a <c>params T[]</c> is an array creation.  <see cref="Bind"/> propagates a
    /// <see langword="null"/> result to its own <see langword="null"/> return, so callers land on BCF1003
    /// rather than silently emitting a childless element.
    /// </summary>
    /// <remarks>
    /// The array-creation arm is defensive: all three of the surfaces this analyzer knows declare
    /// <c>params ReadOnlySpan&lt;View&gt;</c>, so no current call reaches it.  It is kept because the
    /// <c>params T[]</c> shape is the other half of the language rule, and because dropping it would make
    /// adding such a parameter a silent childless emit rather than a compile-time gap.
    /// </remarks>
    private static ImmutableArray<ExpressionSyntax>? ExtractParamsElements(IArgumentOperation argument) =>
        argument.Value switch
        {
            ICollectionExpressionOperation collection => ExtractElements(collection.Elements),
            IArrayCreationOperation { Initializer: { } initializer } =>
                ExtractElements(initializer.ElementValues),
            _ => null,
        };

    /// <summary>
    /// Recovers the children of a collection-expression literal passed whole to a <c>params</c>
    /// parameter (<c>Div[["a", "b"]]</c>), or <see langword="null"/> when the argument is any other
    /// shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The literal arrives wrapped in the conversion to the parameter's type — <see cref="ArgumentKind"/>
    /// <c>Explicit</c> carrying an <see cref="IConversionOperation"/> whose operand is the
    /// <see cref="ICollectionExpressionOperation"/> — so the conversion is unwrapped before matching.  An
    /// expanded bucket is synthesized and carries no conversion, which is why
    /// <see cref="ExtractParamsElements"/> needs no such step.
    /// </para>
    /// <para>
    /// Deliberately narrower than <see cref="ExtractParamsElements"/>: it does not accept the
    /// <see cref="IArrayCreationOperation"/> shape.  That arm is for a <em>synthesized</em> array, whose
    /// elements each still sit inside their own <see cref="ArgumentSyntax"/>.  A written
    /// <c>Div[new View[] { … }]</c> reaches the same operation shape, but its elements' nearest container
    /// is the whole argument — measured — so <see cref="TryRecoverElementExpression"/> would hand back the
    /// entire array-creation expression for every child.  Accepting only the literal keeps that shape on
    /// BCF1003, where #75 left it.
    /// </para>
    /// </remarks>
    private static ImmutableArray<ExpressionSyntax>? TryExtractLiteralElements(IArgumentOperation argument)
    {
        var value = argument.Value is IConversionOperation conversion ? conversion.Operand : argument.Value;

        return value is ICollectionExpressionOperation collection
            ? ExtractElements(collection.Elements)
            : null;
    }

    /// <summary>
    /// Maps each element operation to the expression the author wrote for it, or returns
    /// <see langword="null"/> when any element's written expression cannot be recovered.  All-or-nothing
    /// on purpose: a partially recovered child list would emit an element missing children the author
    /// wrote — <c>Div[["a", ..items]]</c> would become a one-child element instead of a rejection.
    /// </summary>
    private static ImmutableArray<ExpressionSyntax>? ExtractElements(ImmutableArray<IOperation> elements)
    {
        var builder = ImmutableArray.CreateBuilder<ExpressionSyntax>(elements.Length);

        foreach (var element in elements)
        {
            if (!TryRecoverElementExpression(element, out var syntax))
                return null;

            builder.Add(syntax);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Recovers the expression the author actually wrote for one element of a child list, whether that
    /// list came from an expanded <c>params</c> bucket or from a collection-expression literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk exists because a bare null-forgiving element with nothing else to convert (e.g. the
    /// <c>NullText!</c> in <c>Div[NullText!]</c>) has its <c>!</c> elided from the operation tree, so
    /// <paramref name="element"/>'s <c>Syntax</c> points at the inner operand rather than the written
    /// expression.  Climb to the container that holds exactly one child and take its expression.
    /// </para>
    /// <para>
    /// There are two such containers, and which one appears depends on how the children were written: an
    /// <see cref="ArgumentSyntax"/> in the expanded form (<c>Div[NullText!]</c>), an
    /// <see cref="ExpressionElementSyntax"/> inside the literal form (<c>Div[[NullText!]]</c>).  Matching
    /// only <see cref="ArgumentSyntax"/> — as this did before #75 — silently resolves <em>every</em>
    /// element of a literal to the whole outer argument, because the walk climbs straight past the
    /// literal's element containers.
    /// </para>
    /// <para>
    /// A <see cref="SpreadElementSyntax"/> stops the walk instead of satisfying it: a spread's items are a
    /// runtime collection, so there is no per-child written expression to return, and continuing would
    /// reach the enclosing <see cref="ArgumentSyntax"/> and hand back the whole literal as if it were one
    /// child.  Keyed on the syntax rather than on <c>ISpreadOperation</c> so that this one method owns the
    /// whole question of which container holds a child's written expression, and because the syntax
    /// records what the author wrote whether or not the spread's operand bound.  No input was found where
    /// the two keys disagree: a spread with an unbound operand was measured to make the entire element
    /// access an <c>IInvalidOperation</c>, so <see cref="Bind"/> returns before any element is examined.
    /// </para>
    /// <para>
    /// The walk also stops at the containers of those containers — a
    /// <see cref="CollectionExpressionSyntax"/> or a <see cref="BaseArgumentListSyntax"/>.  On every shape
    /// measured for #75 one of the two child containers is reached first, so these arms are unreachable
    /// today.  They are here because the walk is otherwise unbounded: a shape whose element syntax sat
    /// outside its own container would climb to an <em>enclosing</em> call's argument and emit an
    /// unrelated expression as a child.  Failing to BCF1003 is the right answer for anything this method
    /// does not recognize.
    /// </para>
    /// </remarks>
    private static bool TryRecoverElementExpression(
        IOperation element, [MaybeNullWhen(false)] out ExpressionSyntax syntax)
    {
        for (SyntaxNode? node = element.Syntax; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case ExpressionElementSyntax expressionElement:
                    syntax = expressionElement.Expression;
                    return true;

                case ArgumentSyntax argument:
                    syntax = argument.Expression;
                    return true;

                case SpreadElementSyntax:
                case CollectionExpressionSyntax:
                case BaseArgumentListSyntax:
                    syntax = null;
                    return false;
            }
        }

        syntax = null;
        return false;
    }
}

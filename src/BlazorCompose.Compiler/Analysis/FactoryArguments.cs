using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace BlazorCompose.Compiler.Analysis;

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
        bool hasExplicitParamsArgument)
    {
        _byDeclaredParameter = byDeclaredParameter;
        ParamsElements = paramsElements;
        HasExplicitParamsArgument = hasExplicitParamsArgument;
    }

    /// <summary>
    /// The children written into a <c>params</c> parameter in expanded form, in source order.  Empty when
    /// the callee has no <c>params</c> parameter or it received no elements.
    /// </summary>
    internal ImmutableArray<ExpressionSyntax> ParamsElements { get; }

    /// <summary>
    /// True when the <c>params</c> parameter received one whole collection (<c>Div(children: arr)</c>)
    /// instead of expanded elements.  Such an argument is a collection expression, not a list of
    /// children, so callers must reject it rather than mis-split it.
    /// </summary>
    internal bool HasExplicitParamsArgument { get; }

    /// <summary>
    /// The argument bound to the declared parameter at <paramref name="index"/>, ignoring an extension
    /// method's receiver, or <see langword="null"/> when that parameter received no argument (an omitted
    /// optional) or when <paramref name="index"/> is out of range.
    /// </summary>
    internal ArgumentSyntax? At(int index) =>
        (uint)index < (uint)_byDeclaredParameter.Length ? _byDeclaredParameter[index] : null;

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
        var declaredCount = method.Parameters.Length - offset;
        if (declaredCount < 0)
            return null;

        var byParameter = new ArgumentSyntax?[declaredCount];
        var paramsElements = ImmutableArray<ExpressionSyntax>.Empty;
        var hasExplicitParams = false;

        foreach (var argument in operation.Arguments)
        {
            if (argument.Parameter is not { } parameter)
                return null;

            var index = parameter.Ordinal - offset;
            if (index < 0)
                continue;                                  // the extension receiver

            switch (argument.ArgumentKind)
            {
                case ArgumentKind.DefaultValue:
                    continue;                              // omitted optional: leave the slot null

                case ArgumentKind.ParamCollection:
                case ArgumentKind.ParamArray:
                    paramsElements = ExtractParamsElements(argument);
                    continue;

                default:
                    // Ordinarily argument.Syntax IS the ArgumentSyntax. But when the argument
                    // expression is a bare null-forgiving suppression with nothing else to convert
                    // (e.g. `Class(NullClass!)`), Roslyn elides the suppression operator from the
                    // operation tree and Syntax points at the innermost operand instead — so look for
                    // the enclosing ArgumentSyntax rather than requiring an exact match.
                    if (argument.Syntax.FirstAncestorOrSelf<ArgumentSyntax>() is not { } argumentSyntax)
                        return null;

                    if (parameter.IsParams)
                        hasExplicitParams = true;
                    else
                        byParameter[index] = argumentSyntax;

                    continue;
            }
        }

        return new FactoryArguments(
            ImmutableArray.Create(byParameter), paramsElements, hasExplicitParams);
    }

    /// <summary>
    /// Unwraps the synthesized collection an expanded <c>params</c> bucket is modelled as, returning each
    /// child's own syntax.  A <c>params ReadOnlySpan&lt;View&gt;</c> is a collection expression; a
    /// <c>params T[]</c> is an array creation.  Anything else yields no children, which callers surface
    /// as a translation failure rather than as a wrong child list.
    /// </summary>
    private static ImmutableArray<ExpressionSyntax> ExtractParamsElements(IArgumentOperation argument)
    {
        var builder = ImmutableArray.CreateBuilder<ExpressionSyntax>();

        switch (argument.Value)
        {
            case ICollectionExpressionOperation collection:
                foreach (var element in collection.Elements)
                {
                    if (element.Syntax is not ExpressionSyntax syntax)
                        return [];

                    builder.Add(syntax);
                }

                break;

            case IArrayCreationOperation { Initializer: { } initializer }:
                foreach (var element in initializer.ElementValues)
                {
                    if (element.Syntax is not ExpressionSyntax syntax)
                        return [];

                    builder.Add(syntax);
                }

                break;

            default:
                return [];
        }

        return builder.ToImmutable();
    }
}

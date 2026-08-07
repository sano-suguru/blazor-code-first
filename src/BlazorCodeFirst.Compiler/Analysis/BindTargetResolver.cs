using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Why a <c>.Bind</c> getter cannot be used as written.
/// </summary>
internal enum BindTargetFailure
{
    None,

    /// <summary>Not an inline lambda with an expression body. BCF3017.</summary>
    NotInlineExpressionLambda,

    /// <summary>An inline lambda, but its body cannot be assigned to. BCF3018.</summary>
    NotAssignable,
}

/// <summary>
/// Reads a <c>.Bind</c> getter argument. The getter is an inline lambda whose body is transplanted
/// twice — as the bound attribute's value and as the binder's current value — and, in the two-argument
/// form, placed on the left of an assignment to derive the setter.
/// </summary>
/// <remarks>
/// Shared by the element surface and the component surface, which apply the same two rules to the same
/// argument shape and would otherwise agree only by coincidence.
/// </remarks>
internal static class BindTargetResolver
{
    /// <summary>
    /// Extracts the getter's body expression.
    /// </summary>
    /// <param name="getter">The syntax written in the getter argument position.</param>
    /// <param name="body">The lambda's body expression, when it has one.</param>
    /// <returns><see cref="BindTargetFailure.NotInlineExpressionLambda"/> or <see cref="BindTargetFailure.None"/>.</returns>
    public static BindTargetFailure TryGetBody(ExpressionSyntax getter, out ExpressionSyntax? body)
    {
        body = null;

        // () => _name  — the only accepted shape. A parenthesized lambda with no parameters.
        // Rejected: () => { return _name; } (block body), GetName (method group), delegate {} (anonymous method).
        if (getter is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 0 } lambda
            && lambda.ExpressionBody is { } expression)
        {
            body = expression;
            return BindTargetFailure.None;
        }

        return BindTargetFailure.NotInlineExpressionLambda;
    }

    /// <summary>
    /// Whether <paramref name="body"/> can appear on the left of an assignment in generated code, so
    /// that the two-argument form can derive a setter from it.
    /// </summary>
    /// <remarks>
    /// A local, a parameter, and a <c>ForEach</c> iteration variable are rejected even though C# would
    /// assign to them: <c>Body</c> is a property getter, so those die with each render and the write-back
    /// would not survive to the next one. A <c>[Composable]</c> parameter is rejected by the same arm and
    /// for a sharper reason: expansion replaces it with a generated local holding a copy of the caller's
    /// argument, so an inverted setter would assign to that copy and the caller's own field would never
    /// see the value. A <em>member</em> of any of them (<c>o.Title</c>) is accepted, because that writes
    /// through to the object the copied reference names.
    /// </remarks>
    public static BindTargetFailure CheckAssignable(
        ExpressionSyntax body, SemanticModel semanticModel, System.Threading.CancellationToken cancellationToken)
    {
        // Element access (_dict["k"], _list[i]) is assignable when the indexer has a setter, and when the
        // receiver is an array.
        if (body is ElementAccessExpressionSyntax)
        {
            var indexer = semanticModel.GetSymbolInfo(body, cancellationToken).Symbol;
            return indexer switch
            {
                null => BindTargetFailure.None,                      // array element access binds no symbol
                IPropertySymbol { SetMethod: not null } => BindTargetFailure.None,
                _ => BindTargetFailure.NotAssignable,
            };
        }

        // Identifier (_name, Name) or member access (this._name, _form.Name, Model.Items[0].Title).
        if (body is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return BindTargetFailure.NotAssignable;

        var symbol = semanticModel.GetSymbolInfo(body, cancellationToken).Symbol;
        return symbol switch
        {
            IFieldSymbol { IsReadOnly: false, IsConst: false } => BindTargetFailure.None,
            IPropertySymbol { SetMethod: not null } => BindTargetFailure.None,
            // A local or a parameter reached directly: assignable in C#, dead by the next render.
            ILocalSymbol or IParameterSymbol => BindTargetFailure.NotAssignable,
            _ => BindTargetFailure.NotAssignable,
        };
    }
}

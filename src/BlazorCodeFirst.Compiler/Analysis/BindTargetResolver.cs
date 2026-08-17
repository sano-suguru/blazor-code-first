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
/// twice — as the bound attribute's value and as the binder's current value — and, in the getter-only
/// form, placed on the left of an assignment to derive the setter.
/// </summary>
/// <remarks>
/// Shared by the element surface and the component surface, which apply the same two rules to the same
/// argument shape and would otherwise agree only by coincidence. That sharing is why the forms are named
/// getter-only and explicit-setter rather than by argument count: the counts differ between the two
/// surfaces (three and four on an element, two and three on a component), so a count written here would
/// be wrong for half the callers.
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
    /// that the getter-only form can derive a setter from it.
    /// </summary>
    /// <remarks>
    /// A local, a parameter, and a <c>ForEach</c> iteration variable are rejected even though C# would
    /// assign to them: <c>Body</c> is a property getter, so those die with each render and the write-back
    /// would not survive to the next one. A <c>[ViewPart]</c> parameter is rejected by the same arm and
    /// for a sharper reason: expansion replaces it with a generated local holding a copy of the caller's
    /// argument, so an inverted setter would assign to that copy and the caller's own field would never
    /// see the value. A <em>member</em> of any of them (<c>o.Title</c>) is accepted, because that writes
    /// through to the object the copied reference names.
    /// <para>
    /// Not a pure predicate: accepting a property inside a <c>[ViewPart]</c> records an access requirement
    /// on <paramref name="context"/>, for the reason <see cref="CheckSettable"/> gives.
    /// </para>
    /// </remarks>
    public static BindTargetFailure CheckAssignable(ExpressionSyntax body, ViewPartBodyContext context)
    {
        // Element access (_dict["k"], _list[i]) is assignable when the indexer has a callable setter, and
        // when the receiver is an array.
        if (body is ElementAccessExpressionSyntax)
        {
            var indexer = context.SemanticModel.GetSymbolInfo(body, context.CancellationToken).Symbol;
            return indexer switch
            {
                null => BindTargetFailure.None,                      // array element access binds no symbol
                IPropertySymbol indexerProperty => CheckSettable(indexerProperty, context),
                _ => BindTargetFailure.NotAssignable,
            };
        }

        // Identifier (_name, Name) or member access (this._name, _form.Name, Model.Items[0].Title).
        if (body is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return BindTargetFailure.NotAssignable;

        var symbol = context.SemanticModel.GetSymbolInfo(body, context.CancellationToken).Symbol;
        return symbol switch
        {
            IFieldSymbol { IsReadOnly: false, IsConst: false } => BindTargetFailure.None,
            IPropertySymbol property => CheckSettable(property, context),
            // A local or a parameter reached directly: assignable in C#, dead by the next render.
            ILocalSymbol or IParameterSymbol => BindTargetFailure.NotAssignable,
            _ => BindTargetFailure.NotAssignable,
        };
    }

    /// <summary>
    /// Whether the derived assignment can call <paramref name="property"/>'s setter: it has to exist, it
    /// must not be an <c>init</c> accessor, and the code being generated has to be able to reach it.
    /// </summary>
    /// <remarks>
    /// A setter existing is not the same question as the derived assignment being able to call it (#391).
    /// The derived setter is a lambda, <c>__value =&gt; Name = __value</c>, so an <c>init</c> accessor is
    /// out of reach — C# admits one only in an object initializer, a constructor, or another <c>init</c>
    /// accessor — and so is a setter declared narrower than the property that carries it.
    /// <para>
    /// Which site has to reach it depends on the position. A component's own <c>Body</c> becomes a
    /// <c>RenderView</c> in a partial of that same component, so the site is
    /// <see cref="ViewPartBodyContext.ContainingType"/> and the compilation answers directly: that is why
    /// <c>{ get; private set; }</c> declared on the component is accepted and the same property on
    /// another type is not. A <c>[ViewPart]</c> body is written into call sites this analysis has not
    /// seen, so it records the requirement and lets expansion refuse the sites that cannot reach the
    /// setter, the same route every non-public member the body names outright already takes.
    /// </para>
    /// </remarks>
    private static BindTargetFailure CheckSettable(IPropertySymbol property, ViewPartBodyContext context)
    {
        if (property.SetMethod is not { IsInitOnly: false } setter)
            return BindTargetFailure.NotAssignable;

        if (context.IsInlinedAtCallSites)
        {
            ExpressionTemplateFactory.RecordAccessRequirement(setter, context);
            return BindTargetFailure.None;
        }

        return context.SemanticModel.Compilation.IsSymbolAccessibleWithin(setter, context.ContainingType)
            ? BindTargetFailure.None
            : BindTargetFailure.NotAssignable;
    }
}

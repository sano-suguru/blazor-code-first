using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Resolved <see cref="IMethodSymbol"/> references for the <c>BlazorCompose.UI</c> factory methods so
/// that expression analysis can compare symbols by identity rather than by name.
/// </summary>
/// <remarks>
/// Resolved transiently from a single <see cref="Compilation"/> inside the syntax-provider transforms
/// that consume it and never stored in the cached incremental pipeline, so its symbols are only ever
/// compared within the compilation they came from — never across compilations.  It therefore needs no
/// value equality of its own.
/// </remarks>
internal sealed class KnownSymbols
{
    /// <summary>Resolved symbol for <c>BlazorCompose.UI.Text(string)</c>, or <see langword="null"/> if unavailable.</summary>
    public IMethodSymbol? TextMethod { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.UI.Button(string, Action)</c>, or <see langword="null"/> if unavailable.</summary>
    public IMethodSymbol? ButtonMethod { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.UI.VStack(params ReadOnlySpan&lt;View&gt;)</c>, or <see langword="null"/> if unavailable.</summary>
    public IMethodSymbol? VStackMethod { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.UI.If(bool, Func&lt;View&gt;, Func&lt;View&gt;?)</c>, or <see langword="null"/> if unavailable.</summary>
    public IMethodSymbol? IfMethod { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.UI.ForEach&lt;T&gt;(IEnumerable&lt;T&gt;, Func&lt;T, object?&gt;, Func&lt;T, View&gt;)</c>, or null.</summary>
    public IMethodSymbol? ForEachMethod { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.View</c>, or <see langword="null"/> if unavailable.</summary>
    public INamedTypeSymbol? ViewType { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.ComposableAttribute</c>, or <see langword="null"/> if unavailable.</summary>
    public INamedTypeSymbol? ComposableAttributeType { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.UI.Component&lt;T&gt;()</c>, or null.</summary>
    public IMethodSymbol? ComponentMethod { get; }

    /// <summary>Resolved unbound generic <c>BlazorCompose.ComponentView&lt;T&gt;</c>, or null.</summary>
    public INamedTypeSymbol? ComponentViewType { get; }

    /// <summary>Resolved symbol for <c>ComponentView&lt;T&gt;.Param&lt;TValue&gt;(...)</c>, or null.</summary>
    public IMethodSymbol? ParamMethod { get; }

    /// <summary>Resolved <c>Microsoft.AspNetCore.Components.ParameterAttribute</c>, or null.</summary>
    public INamedTypeSymbol? ParameterAttributeType { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.Decorations.Class(this View, string)</c>, or null.</summary>
    public IMethodSymbol? ClassMethod { get; }

    private KnownSymbols(INamedTypeSymbol uiType, Compilation compilation)
    {
        ViewType = uiType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.View");
        ComposableAttributeType =
            uiType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.ComposableAttribute");
        ComponentViewType = uiType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.ComponentView`1");
        ParameterAttributeType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.ParameterAttribute");

        if (ComponentViewType is not null)
        {
            foreach (var member in ComponentViewType.GetMembers("Param"))
            {
                if (member is IMethodSymbol { Arity: 1, Parameters.Length: 2 } paramMethod)
                {
                    ParamMethod = paramMethod;
                    break;
                }
            }
        }

        var decorationsType =
            uiType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.Decorations");
        if (decorationsType is not null)
        {
            foreach (var member in decorationsType.GetMembers("Class"))
            {
                // Unreduced extension method: (this View, string), arity 0.
                if (member is IMethodSymbol { IsExtensionMethod: true, Arity: 0, Parameters.Length: 2 } classMethod)
                {
                    ClassMethod = classMethod;
                    break;
                }
            }
        }

        foreach (var member in uiType.GetMembers())
        {
            if (member is not IMethodSymbol method)
                continue;

            switch (method.Name)
            {
                case "Text" when method.Parameters.Length == 1:
                    TextMethod = method;
                    break;
                case "Button" when method.Parameters.Length == 2:
                    ButtonMethod = method;
                    break;
                case "VStack" when method.Parameters.Length == 1 && method.Parameters[0].IsParams:
                    VStackMethod = method;
                    break;
                case "If" when method.Parameters.Length == 3:
                    IfMethod = method;
                    break;
                case "ForEach" when method.Parameters.Length == 3 && method.Arity == 1:
                    ForEachMethod = method;
                    break;
                case "Component" when method.Arity == 1 && method.Parameters.Length == 0:
                    ComponentMethod = method;
                    break;
            }
        }
    }

    /// <summary>
    /// Resolves <c>BlazorCompose.UI</c> from the given compilation and returns a populated instance,
    /// or <see langword="null"/> when the type cannot be found (e.g., the runtime assembly is not referenced).
    /// </summary>
    public static KnownSymbols? TryCreate(Compilation compilation)
    {
        var uiType = compilation.GetTypeByMetadataName("BlazorCompose.UI");
        return uiType is not null ? new KnownSymbols(uiType, compilation) : null;
    }

}

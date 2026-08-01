using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Resolved symbol references for the <c>BlazorCompose.Html</c> factories and their surrounding types so
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
    /// <summary>Resolved symbol for <c>BlazorCompose.View</c>, or <see langword="null"/> if unavailable.</summary>
    public INamedTypeSymbol? ViewType { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.ComposableAttribute</c>, or <see langword="null"/> if unavailable.</summary>
    public INamedTypeSymbol? ComposableAttributeType { get; }

    /// <summary>Resolved unbound generic <c>BlazorCompose.ComponentView&lt;T&gt;</c>, or null.</summary>
    public INamedTypeSymbol? ComponentViewType { get; }

    /// <summary>
    /// Resolved symbol for <c>BlazorCompose.ElementBuilder</c>, or <see langword="null"/> — which is the
    /// normal case against a runtime that has not adopted the bracket surface.
    /// </summary>
    /// <remarks>
    /// Every consumer must guard on this being non-null before comparing against it.
    /// <c>SymbolEqualityComparer.Default.Equals(x, null)</c> answers <see langword="true"/> for a null
    /// <c>x</c>, so an unguarded comparison would classify an unrelated indexer — <c>_dict["k"]</c> — as an
    /// element.
    /// </remarks>
    public INamedTypeSymbol? ElementBuilderType { get; }

    /// <summary>
    /// Resolved symbol for <c>ElementBuilder</c>'s <c>params ReadOnlySpan&lt;View&gt;</c> indexer, which is
    /// how children are written on the bracket surface, or null.
    /// </summary>
    public IPropertySymbol? ElementIndexer { get; }

    /// <summary>
    /// Resolved symbol for <c>ComponentView&lt;T&gt;</c>'s <c>params ReadOnlySpan&lt;View&gt;</c> indexer,
    /// which replaces the <c>Component&lt;T&gt;(children)</c> overload on the bracket surface, or null.
    /// </summary>
    public IPropertySymbol? ComponentIndexer { get; }

    /// <summary>Resolved symbol for <c>ComponentView&lt;T&gt;.Param&lt;TValue&gt;(...)</c>, or null.</summary>
    public IMethodSymbol? ParamMethod { get; }

    /// <summary>
    /// Resolved symbol for <c>ComponentView&lt;T&gt;.Param(Func&lt;T, RenderFragment?&gt;, View)</c>, or null.
    /// </summary>
    public IMethodSymbol? FragmentParamMethod { get; }

    /// <summary>Resolved <c>Microsoft.AspNetCore.Components.ParameterAttribute</c>, or null.</summary>
    public INamedTypeSymbol? ParameterAttributeType { get; }

    /// <summary>Resolved symbol for <c>Microsoft.AspNetCore.Components.RenderFragment</c>, or null.</summary>
    /// <remarks>
    /// The non-generic delegate only. <c>RenderFragment&lt;T&gt;</c> has metadata name
    /// <c>RenderFragment`1</c> and has no conversion to View, so it is rejected by the C# compiler
    /// (CS1503) and never reaches the analyzer.
    /// </remarks>
    public INamedTypeSymbol? RenderFragmentType { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.Decorations.Class(this View, string)</c>, or null.</summary>
    public IMethodSymbol? ClassMethod { get; }

    /// <summary>Authoritative curated element helper name → HTML tag table. The compiler owns this map;
    /// runtime helper declarations are kept in sync by KnownSymbolsSyncTests.</summary>
    private static readonly Dictionary<string, string> CuratedTags = new(System.StringComparer.Ordinal)
    {
        ["Div"] = "div",
        ["Span"] = "span",
        ["Button"] = "button",
        ["Nav"] = "nav",
        ["Header"] = "header",
        ["Main"] = "main",
        ["Aside"] = "aside",
        ["Footer"] = "footer",
        ["Section"] = "section",
        ["Article"] = "article",
        ["P"] = "p",
        ["H1"] = "h1",
        ["H2"] = "h2",
        ["H3"] = "h3",
        ["H4"] = "h4",
        ["H5"] = "h5",
        ["H6"] = "h6",
        ["Ul"] = "ul",
        ["Ol"] = "ol",
        ["Li"] = "li",
        ["A"] = "a",
        ["Img"] = "img",
    };

    /// <summary>Named attribute shortcut method name → attribute name.</summary>
    private static readonly Dictionary<string, string> AttributeShortcutNames = new(System.StringComparer.Ordinal)
    {
        ["Href"] = "href",
        ["Src"] = "src",
        ["Alt"] = "alt",
        ["Id"] = "id",
        ["Type"] = "type",
        ["Title"] = "title",
        ["Role"] = "role",
    };

    /// <summary>Normalizes a method to the comparable key used in every map: reduced extension methods
    /// (fluent decorations) are unreduced, then the original definition is taken.</summary>
    public static ISymbol Normalize(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;

    /// <summary>
    /// Normalizes a property to the comparable key used in <see cref="ElementTags"/>: an element factory
    /// spelled as a property has nothing to unreduce, so only the original definition is taken.
    /// </summary>
    /// <remarks>
    /// Deliberately a second overload rather than one <c>Normalize(ISymbol)</c>: an
    /// <see cref="ISymbol"/>-typed parameter would silently win overload resolution at the method call
    /// sites, stop walking <see cref="IMethodSymbol.ReducedFrom"/>, and key every fluent decoration under
    /// its reduced symbol — which no map contains.
    /// </remarks>
    public static ISymbol Normalize(IPropertySymbol property) => property.OriginalDefinition;

    /// <summary>
    /// Whether <paramref name="method"/> is either <c>Html.Element</c> overload.  Both must be matched: the
    /// arities are distinct symbols, and missing one drops every call site written that way to BC1003.
    /// </summary>
    public bool IsElementFactory(IMethodSymbol method) =>
        (HtmlElement is not null
            && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, HtmlElement))
        || (HtmlElementWithChildren is not null
            && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, HtmlElementWithChildren));

    /// <summary>Curated element helper method → HTML tag name.</summary>
    public IReadOnlyDictionary<ISymbol, string> ElementTags { get; }

    /// <summary>Named attribute shortcut decoration method → attribute name.</summary>
    public IReadOnlyDictionary<ISymbol, string> AttributeShortcuts { get; }

    /// <summary>Named event shortcut decoration method (e.g. <c>OnClick</c> overloads) → HTML event name.</summary>
    public IReadOnlyDictionary<ISymbol, string> EventShortcuts { get; }

    /// <summary>All <c>Decorations.Attr</c> overloads.</summary>
    public IReadOnlyCollection<ISymbol> AttrMethods { get; }

    /// <summary>All <c>Decorations.On</c> overloads.</summary>
    public IReadOnlyCollection<ISymbol> OnMethods { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.Html.Element(string)</c>, or null.</summary>
    public IMethodSymbol? HtmlElement { get; }

    /// <summary>
    /// Resolved symbol for <c>Html.Element(string, params ReadOnlySpan&lt;View&gt;)</c>, or null.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate field from <see cref="HtmlElement"/>, as
    /// <see cref="HtmlComponentWithChildren"/> is from <see cref="HtmlComponent"/>.  One field matching both
    /// arities cannot work: a runtime declaring both would let <c>GetMembers</c> order decide which overload
    /// analysis recognizes, and the other would fall through to BC1003.  Match them through
    /// <see cref="IsElementFactory"/> rather than by hand, so no consumer can OR only one.
    /// </remarks>
    public IMethodSymbol? HtmlElementWithChildren { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.Html.If(bool, Func&lt;View&gt;, Func&lt;View&gt;?)</c>, or null.</summary>
    public IMethodSymbol? HtmlIf { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.Html.ForEach&lt;T&gt;(...)</c>, or null.</summary>
    public IMethodSymbol? HtmlForEach { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.Html.Component&lt;T&gt;()</c>, or null.</summary>
    public IMethodSymbol? HtmlComponent { get; }

    /// <summary>
    /// Resolved symbol for <c>Html.Component&lt;T&gt;(params ReadOnlySpan&lt;View&gt;)</c>, or null.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate field from <see cref="HtmlComponent"/>: every consumer must OR-match both,
    /// or the params form falls through analysis to BC1003 and its BC3012 sweep goes silent.
    /// </remarks>
    public IMethodSymbol? HtmlComponentWithChildren { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.Html.Raw(string)</c>, or null.</summary>
    public IMethodSymbol? HtmlRaw { get; }

    /// <summary>Resolved symbol for <c>BlazorCompose.Html.Fragment(params ReadOnlySpan&lt;View&gt;)</c>, or null.</summary>
    public IMethodSymbol? HtmlFragment { get; }

    private KnownSymbols(INamedTypeSymbol htmlType, Compilation compilation)
    {
        ViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.View");
        ComposableAttributeType =
            htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.ComposableAttribute");
        ComponentViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.ComponentView`1");
        ElementBuilderType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.ElementBuilder");
        ElementIndexer = FindChildrenIndexer(ElementBuilderType);
        ComponentIndexer = FindChildrenIndexer(ComponentViewType);
        ParameterAttributeType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.ParameterAttribute");
        RenderFragmentType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.RenderFragment");

        if (ComponentViewType is not null)
        {
            foreach (var member in ComponentViewType.GetMembers("Param"))
            {
                if (member is not IMethodSymbol { Parameters.Length: 2 } paramMethod)
                    continue;

                // Arity discriminates the two overloads: the scalar one is Param<TValue>, the fragment
                // one is non-generic. Do not break early — both must be captured.
                if (paramMethod.Arity == 1)
                    ParamMethod = paramMethod;
                else if (paramMethod.Arity == 0)
                    FragmentParamMethod = paramMethod;
            }
        }

        var decorationsType =
            htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCompose.Decorations");

        var attributeShortcuts = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var eventShortcuts = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var attrMethods = new List<ISymbol>();
        var onMethods = new List<ISymbol>();
        if (decorationsType is not null)
        {
            foreach (var member in decorationsType.GetMembers())
            {
                if (member is not IMethodSymbol { IsExtensionMethod: true } method)
                    continue;
                var key = Normalize(method);
                switch (method.Name)
                {
                    case "Class" when method.Parameters.Length == 2: ClassMethod = method; break;
                    case "OnClick":
                        // Both overloads map to "onclick"; the analyzer's decoration branch dispatches
                        // on EventShortcuts, so no separate first-overload symbol is retained.
                        eventShortcuts[key] = "onclick";
                        break;
                    case "On": onMethods.Add(key); break;                     // both overloads
                    case "Attr": attrMethods.Add(key); break;
                    default:
                        if (AttributeShortcutNames.TryGetValue(method.Name, out var attr))
                            attributeShortcuts[key] = attr;
                        break;
                }
            }
        }
        AttributeShortcuts = attributeShortcuts;
        EventShortcuts = eventShortcuts;
        AttrMethods = attrMethods;
        OnMethods = onMethods;

        var elementTags = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        foreach (var member in htmlType.GetMembers())
        {
            // A curated helper is a method on the current surface and a property returning ElementBuilder on
            // the bracket surface. Both are matched so this map populates either way; #87 removes the method
            // arm below once the properties ship.
            if (member is IPropertySymbol { IsIndexer: false } elementProperty)
            {
                if (CuratedTags.TryGetValue(elementProperty.Name, out var propertyTag))
                    elementTags[Normalize(elementProperty)] = propertyTag;

                continue;
            }

            if (member is not IMethodSymbol method)
                continue;
            switch (method.Name)
            {
                // One parameter is the bracket surface's Element(string tag) returning ElementBuilder; two
                // is the current Element(string tag, params ReadOnlySpan<View>). Distinct fields, matched
                // together by IsElementFactory; #87 removes the two-parameter arm once the properties ship.
                case "Element" when method.Parameters.Length == 1: HtmlElement = method; break;
                case "Element" when method.Parameters.Length == 2: HtmlElementWithChildren = method; break;
                case "If" when method.Parameters.Length == 3: HtmlIf = method; break;
                case "ForEach" when method.Parameters.Length == 3 && method.Arity == 1: HtmlForEach = method; break;
                case "Component" when method.Arity == 1 && method.Parameters.Length == 0:
                    HtmlComponent = method;
                    break;
                case "Component" when method.Arity == 1
                        && method.Parameters.Length == 1
                        && method.Parameters[0].IsParams:
                    HtmlComponentWithChildren = method;
                    break;
                case "Raw" when method.Parameters.Length == 1: HtmlRaw = method; break;
                case "Fragment" when method.Parameters.Length == 1: HtmlFragment = method; break;
                default:
                    if (CuratedTags.TryGetValue(method.Name, out var tag))
                        elementTags[Normalize(method)] = tag;
                    break;
            }
        }
        ElementTags = elementTags;
    }

    /// <summary>
    /// The single-<c>params</c>-parameter indexer declared on <paramref name="type"/>, which is how children
    /// are written on the bracket surface, or <see langword="null"/> when the type is absent or declares no
    /// such indexer.
    /// </summary>
    private static IPropertySymbol? FindChildrenIndexer(INamedTypeSymbol? type)
    {
        if (type is null)
            return null;

        foreach (var member in type.GetMembers())
        {
            if (member is IPropertySymbol { IsIndexer: true, Parameters.Length: 1 } indexer
                && indexer.Parameters[0].IsParams)
            {
                return indexer;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves <c>BlazorCompose.Html</c> from the given compilation and returns a populated instance,
    /// or <see langword="null"/> when the type cannot be found (e.g., the runtime assembly is not referenced).
    /// </summary>
    public static KnownSymbols? TryCreate(Compilation compilation)
    {
        var htmlType = compilation.GetTypeByMetadataName("BlazorCompose.Html");
        return htmlType is not null ? new KnownSymbols(htmlType, compilation) : null;
    }

}

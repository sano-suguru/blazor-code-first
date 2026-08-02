using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Resolved symbol references for the <c>BlazorCodeFirst.Html</c> design-time syntax and their surrounding
/// types so that expression analysis can compare symbols by identity rather than by name.
/// </summary>
/// <remarks>
/// Resolved transiently from a single <see cref="Compilation"/> inside the syntax-provider transforms
/// that consume it and never stored in the cached incremental pipeline, so its symbols are only ever
/// compared within the compilation they came from — never across compilations.  It therefore needs no
/// value equality of its own.
/// </remarks>
internal sealed class KnownSymbols
{
    /// <summary>Resolved symbol for <c>BlazorCodeFirst.View</c>, or <see langword="null"/> if unavailable.</summary>
    public INamedTypeSymbol? ViewType { get; }

    /// <summary>Resolved symbol for <c>BlazorCodeFirst.ComposableAttribute</c>, or <see langword="null"/> if unavailable.</summary>
    public INamedTypeSymbol? ComposableAttributeType { get; }

    /// <summary>Resolved unbound generic <c>BlazorCodeFirst.ComponentView&lt;T&gt;</c>, or null.</summary>
    public INamedTypeSymbol? ComponentViewType { get; }

    /// <summary>
    /// Resolved symbol for <c>BlazorCodeFirst.ElementBuilder</c>, or <see langword="null"/> — which is the
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
    /// which is the one channel child content reaches a component through, or null.
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

    /// <summary>
    /// Resolved symbol for <c>BlazorCodeFirst.Decorations.Class(this ElementBuilder, string)</c>, or null.
    /// </summary>
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
    /// Normalizes a property to the comparable key used in <see cref="ElementTags"/>: an element helper
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
    /// Whether <paramref name="method"/> is <see cref="HtmlElement"/>, the escape hatch for a tag outside the
    /// curated table.
    /// </summary>
    /// <remarks>
    /// The null guard is defensive consistency, not a correctness requirement:
    /// <see cref="ISymbol.OriginalDefinition"/> is never null, so an unguarded comparison against a null
    /// <see cref="HtmlElement"/> would already answer <see langword="false"/>.  The guard is load-bearing
    /// only where the <em>left</em> operand can be null — a receiver's unresolved <c>GetTypeInfo().Type</c>,
    /// as <see cref="ElementBuilderType"/>'s remarks describe — and the two shapes are kept alike so neither
    /// is read as the other's rule.
    /// </remarks>
    public bool IsElementFactory(IMethodSymbol method) =>
        HtmlElement is not null
        && SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, HtmlElement);

    /// <summary>
    /// The names of every decoration the referenced runtime declares, for
    /// <see cref="DeclaresDecorationNamed"/>.
    /// </summary>
    private readonly HashSet<string> _decorationNames;

    /// <summary>
    /// Whether the referenced runtime's <c>Decorations</c> type declares a decoration spelled
    /// <paramref name="name"/>: <c>Class</c>, a named attribute shortcut, an event shortcut, <c>Attr</c> or
    /// <c>On</c>.
    /// </summary>
    /// <remarks>
    /// The names are read off the symbols resolved out of the referenced runtime assembly rather than from
    /// a literal table, so a user-defined <c>Some.BlazorCodeFirst.Decorations</c> contributes none of them and
    /// a runtime that renames a decoration cannot leave a stale spelling behind here.
    /// <para>
    /// It is nevertheless only a name test, and is <em>never</em> sufficient on its own — any type may
    /// declare a <c>Class</c> or a <c>Title</c>.  Every caller must pair it with symbol-identity checks on
    /// the types involved; it exists to narrow those, not to replace them.  Compare
    /// <c>RenderMutationAnalyzer</c>, which likewise tests a name and then anchors it by resolving the
    /// containing type and its namespace.
    /// </para>
    /// </remarks>
    public bool DeclaresDecorationNamed(string name) => _decorationNames.Contains(name);

    /// <summary>
    /// Curated element helper property → HTML tag name.  Keyed by <see cref="ISymbol"/> rather than
    /// <see cref="IPropertySymbol"/> only because every consumer compares through
    /// <see cref="SymbolEqualityComparer"/>; every key is an <see cref="IPropertySymbol"/>.
    /// </summary>
    public IReadOnlyDictionary<ISymbol, string> ElementTags { get; }

    /// <summary>Named attribute shortcut decoration method → attribute name.</summary>
    public IReadOnlyDictionary<ISymbol, string> AttributeShortcuts { get; }

    /// <summary>Named event shortcut decoration method (e.g. <c>OnClick</c> overloads) → HTML event name.</summary>
    public IReadOnlyDictionary<ISymbol, string> EventShortcuts { get; }

    /// <summary>All <c>Decorations.Attr</c> overloads.</summary>
    public IReadOnlyCollection<ISymbol> AttrMethods { get; }

    /// <summary>All <c>Decorations.On</c> overloads.</summary>
    public IReadOnlyCollection<ISymbol> OnMethods { get; }

    /// <summary>
    /// Resolved symbol for <c>BlazorCodeFirst.Html.Element(string)</c>, which returns an
    /// <c>ElementBuilder</c> and carries no children of its own, or null.
    /// </summary>
    public IMethodSymbol? HtmlElement { get; }

    /// <summary>Resolved symbol for <c>BlazorCodeFirst.Html.If(bool, Func&lt;View&gt;, Func&lt;View&gt;?)</c>, or null.</summary>
    public IMethodSymbol? HtmlIf { get; }

    /// <summary>Resolved symbol for <c>BlazorCodeFirst.Html.ForEach&lt;T&gt;(...)</c>, or null.</summary>
    public IMethodSymbol? HtmlForEach { get; }

    /// <summary>
    /// Resolved symbol for <c>BlazorCodeFirst.Html.Component&lt;T&gt;()</c>, the only component syntax:
    /// children arrive through <see cref="ComponentIndexer"/>, not through an overload.  Null if unavailable.
    /// </summary>
    public IMethodSymbol? HtmlComponent { get; }

    /// <summary>Resolved symbol for <c>BlazorCodeFirst.Html.Raw(string)</c>, or null.</summary>
    public IMethodSymbol? HtmlRaw { get; }

    /// <summary>Resolved symbol for <c>BlazorCodeFirst.Html.Fragment(params ReadOnlySpan&lt;View&gt;)</c>, or null.</summary>
    public IMethodSymbol? HtmlFragment { get; }

    private KnownSymbols(INamedTypeSymbol htmlType, Compilation compilation)
    {
        ViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.View");
        ComposableAttributeType =
            htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ComposableAttribute");
        ComponentViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ComponentView`1");
        ElementBuilderType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ElementBuilder");

        // GetTypeByMetadataName answers null for an *ambiguous* type as well as a missing one — two
        // references both declaring System.ReadOnlySpan<T>, say. Both indexers would then resolve to null and
        // every Div[…] in the compilation would fall through to BCF1003 with nothing naming the cause.
        // ParameterAttributeType and RenderFragmentType degrade the same way, for the same reason.
        var readOnlySpanType = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
        ElementIndexer = FindChildrenIndexer(ElementBuilderType, ViewType, readOnlySpanType);
        ComponentIndexer = FindChildrenIndexer(ComponentViewType, ViewType, readOnlySpanType);
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
            htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.Decorations");

        var attributeShortcuts = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var eventShortcuts = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var attrMethods = new List<ISymbol>();
        var onMethods = new List<ISymbol>();
        if (decorationsType is not null)
        {
            // A decoration is defined by its receiver, not by its name: Decorations declares extension
            // methods on ElementBuilder, and that is what makes .Class/.Attr/.On element decorations
            // rather than members that merely share a name. Capturing by name alone would admit a future
            // overload on another receiver — Attr(this ComponentView<T>, string, string), say — into these
            // sets, where IsDecorationMethod would treat it as an element decoration with nothing to
            // notice. ClassMethod showed the same defect more loudly: a single slot taking whichever
            // two-parameter overload GetMembers returned last.
            //
            // When ElementBuilderType is unavailable the test is skipped rather than failed. Unlike the
            // ambiguous-type scenario above, this lookup (htmlType.ContainingAssembly.GetTypeByMetadataName)
            // is scoped to a single assembly, so null here means that assembly does not declare
            // BlazorCodeFirst.ElementBuilder under that name, not a cross-assembly ambiguity. Rejecting every
            // candidate would still empty these sets and silently disable BCF3008 for every decoration — a
            // worse failure than the one prevented, and an invisible one, where the current degradation at
            // least reports BCF1003.
            foreach (var member in decorationsType.GetMembers())
            {
                if (member is not IMethodSymbol { IsExtensionMethod: true } method)
                    continue;
                if (ElementBuilderType is not null
                    && !(method.Parameters.Length > 0
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, ElementBuilderType)))
                {
                    continue;
                }

                var key = Normalize(method);
                switch (method.Name)
                {
                    // Receiver plus second parameter fully determines (ElementBuilder, string), so this
                    // single slot cannot take an arbitrary overload. Decorations declares exactly one.
                    // Not a list pattern: this project targets netstandard2.0 without a System.Index/Range
                    // polyfill, and the list-pattern lowering requires those types even without a slice.
                    case "Class" when method.Parameters.Length == 2
                        && method.Parameters[1].Type.SpecialType == SpecialType.System_String:
                        ClassMethod = method;
                        break;
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

        _decorationNames = new HashSet<string>(System.StringComparer.Ordinal);
        if (ClassMethod is not null)
            _decorationNames.Add(ClassMethod.Name);
        foreach (var shortcut in attributeShortcuts.Keys)
            _decorationNames.Add(shortcut.Name);
        foreach (var shortcut in eventShortcuts.Keys)
            _decorationNames.Add(shortcut.Name);
        foreach (var attr in attrMethods)
            _decorationNames.Add(attr.Name);
        foreach (var on in onMethods)
            _decorationNames.Add(on.Name);

        var elementTags = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        foreach (var member in htmlType.GetMembers())
        {
            // A curated helper is a property returning ElementBuilder; children are written in brackets on
            // the indexer that type declares. The return type is checked as well as the name: a property
            // that merely shares a curated name is not an element helper.
            if (member is IPropertySymbol { IsIndexer: false } elementProperty)
            {
                if (ElementBuilderType is not null
                    && SymbolEqualityComparer.Default.Equals(elementProperty.Type, ElementBuilderType)
                    && CuratedTags.TryGetValue(elementProperty.Name, out var propertyTag))
                {
                    elementTags[Normalize(elementProperty)] = propertyTag;
                }

                continue;
            }

            if (member is not IMethodSymbol method)
                continue;
            switch (method.Name)
            {
                // Element(string tag) returns an ElementBuilder and is the only arity: children are written
                // in brackets on that builder rather than passed to a second overload.
                case "Element" when method.Parameters.Length == 1: HtmlElement = method; break;
                case "If" when method.Parameters.Length == 3: HtmlIf = method; break;
                case "ForEach" when method.Parameters.Length == 3 && method.Arity == 1: HtmlForEach = method; break;
                case "Component" when method.Arity == 1 && method.Parameters.Length == 0:
                    HtmlComponent = method;
                    break;
                case "Raw" when method.Parameters.Length == 1: HtmlRaw = method; break;
                case "Fragment" when method.Parameters.Length == 1: HtmlFragment = method; break;
            }
        }
        ElementTags = elementTags;
    }

    /// <summary>
    /// The <c>params ReadOnlySpan&lt;View&gt;</c> indexer declared on <paramref name="type"/>, which is how
    /// children are written on the bracket surface, or <see langword="null"/> when either type is absent or
    /// no such indexer is declared.
    /// </summary>
    /// <remarks>
    /// The element type is checked, not just the <c>params</c> shape: an indexer over some other element
    /// type is not the children channel, and matching it would read unrelated arguments as children.
    /// The span type is resolved by identity rather than matched by name, as this class exists to do: a
    /// differently namespaced type also called <c>ReadOnlySpan&lt;T&gt;</c> must not be read as the children
    /// channel.  Compare <c>RenderMutationAnalyzer</c>, which anchors <c>BlazorCodeFirst.Decorations</c> to the
    /// global namespace for the same reason.
    /// </remarks>
    private static IPropertySymbol? FindChildrenIndexer(
        INamedTypeSymbol? type, INamedTypeSymbol? viewType, INamedTypeSymbol? readOnlySpanType)
    {
        if (type is null || viewType is null || readOnlySpanType is null)
            return null;

        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol { IsIndexer: true, Parameters.Length: 1 } indexer
                || !indexer.Parameters[0].IsParams)
            {
                continue;
            }

            if (indexer.Parameters[0].Type is INamedTypeSymbol { TypeArguments.Length: 1 } span
                && SymbolEqualityComparer.Default.Equals(span.OriginalDefinition, readOnlySpanType)
                && SymbolEqualityComparer.Default.Equals(span.TypeArguments[0], viewType))
            {
                return indexer;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves <c>BlazorCodeFirst.Html</c> from the given compilation and returns a populated instance,
    /// or <see langword="null"/> when the type cannot be found (e.g., the runtime assembly is not referenced).
    /// </summary>
    public static KnownSymbols? TryCreate(Compilation compilation)
    {
        var htmlType = compilation.GetTypeByMetadataName("BlazorCodeFirst.Html");
        return htmlType is not null ? new KnownSymbols(htmlType, compilation) : null;
    }

}

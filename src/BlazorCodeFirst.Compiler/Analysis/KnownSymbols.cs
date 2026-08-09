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
/// compared within the compilation they came from, never across compilations. It therefore needs no
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
    /// Resolved symbol for <c>BlazorCodeFirst.ElementBuilder</c>, or <see langword="null"/>, which is the
    /// normal case against a runtime that has not adopted the bracket surface.
    /// </summary>
    /// <remarks>
    /// Every consumer must guard on this being non-null before comparing against it.
    /// <c>SymbolEqualityComparer.Default.Equals(x, null)</c> answers <see langword="true"/> for a null
    /// <c>x</c>, so an unguarded comparison would classify an unrelated indexer, <c>_dict["k"]</c>, as an
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

    /// <summary>Resolved <c>Microsoft.AspNetCore.Components.ParameterAttribute</c>, or null.</summary>
    public INamedTypeSymbol? ParameterAttributeType { get; }

    /// <summary>Resolved symbol for <c>Microsoft.AspNetCore.Components.RenderFragment</c>, or null.</summary>
    public INamedTypeSymbol? RenderFragmentType { get; }

    /// <summary>Resolved unbound generic <c>Microsoft.AspNetCore.Components.RenderFragment&lt;T&gt;</c>, or null.</summary>
    public INamedTypeSymbol? RenderFragmentGenericType { get; }

    /// <summary>
    /// Resolved unbound generic <c>Microsoft.AspNetCore.Components.EventCallback&lt;TValue&gt;</c>, or null.
    /// </summary>
    /// <remarks>
    /// The generic one only. A component binding's change callback is <c>EventCallback&lt;TValue&gt;</c> for
    /// the bound parameter's own type, and the non-generic <c>EventCallback</c> carries no value, so a
    /// parameter declared with it cannot receive the write-back and is BCF3020 like any other mistype.
    /// </remarks>
    public INamedTypeSymbol? EventCallbackType { get; }

    /// <summary>
    /// Resolved unbound generic <c>System.Linq.Expressions.Expression&lt;TDelegate&gt;</c>, or null.
    /// </summary>
    public INamedTypeSymbol? ExpressionType { get; }

    /// <summary>
    /// Resolved unbound generic <c>System.Func&lt;TResult&gt;</c>, the one-argument arity, or null. Paired
    /// with <see cref="ExpressionType"/> to recognize a <c>{name}Expression</c> parameter, whose declared
    /// type is <c>Expression&lt;Func&lt;TValue&gt;&gt;</c>.
    /// </summary>
    public INamedTypeSymbol? FuncType { get; }

    private readonly Dictionary<ISymbol, SurfaceMethodKind> _surfaceMethods;

    /// <summary>Authoritative curated element helper name → HTML tag table. The compiler owns this map;
    /// runtime helper declarations are kept in sync by KnownSymbolsSyncTests.</summary>
    private static readonly Dictionary<string, string> CuratedTags = new(System.StringComparer.Ordinal)
    {
        // Sections
        ["Address"] = "address",
        ["Article"] = "article",
        ["Aside"] = "aside",
        ["Footer"] = "footer",
        ["H1"] = "h1",
        ["H2"] = "h2",
        ["H3"] = "h3",
        ["H4"] = "h4",
        ["H5"] = "h5",
        ["H6"] = "h6",
        ["Header"] = "header",
        ["Hgroup"] = "hgroup",
        ["Main"] = "main",
        ["Nav"] = "nav",
        ["Search"] = "search",
        ["Section"] = "section",

        // Grouping
        ["Blockquote"] = "blockquote",
        ["Dd"] = "dd",
        ["Div"] = "div",
        ["Dl"] = "dl",
        ["Dt"] = "dt",
        ["Figcaption"] = "figcaption",
        ["Figure"] = "figure",
        ["Hr"] = "hr",
        ["Li"] = "li",
        ["Menu"] = "menu",
        ["Ol"] = "ol",
        ["P"] = "p",
        ["Pre"] = "pre",
        ["Ul"] = "ul",

        // Text-level
        ["A"] = "a",
        ["Abbr"] = "abbr",
        ["B"] = "b",
        ["Bdi"] = "bdi",
        ["Bdo"] = "bdo",
        ["Br"] = "br",
        ["Cite"] = "cite",
        ["Code"] = "code",
        ["Data"] = "data",
        ["Dfn"] = "dfn",
        ["Em"] = "em",
        ["I"] = "i",
        ["Kbd"] = "kbd",
        ["Mark"] = "mark",
        ["Q"] = "q",
        ["Rp"] = "rp",
        ["Rt"] = "rt",
        ["Ruby"] = "ruby",
        ["S"] = "s",
        ["Samp"] = "samp",
        ["Small"] = "small",
        ["Span"] = "span",
        ["Strong"] = "strong",
        ["Sub"] = "sub",
        ["Sup"] = "sup",
        ["Time"] = "time",
        ["U"] = "u",
        ["Var"] = "var",
        ["Wbr"] = "wbr",

        // Edits
        ["Del"] = "del",
        ["Ins"] = "ins",

        // Embedded
        ["Area"] = "area",
        ["Audio"] = "audio",
        ["Canvas"] = "canvas",
        ["Embed"] = "embed",
        ["Iframe"] = "iframe",
        ["Img"] = "img",
        ["Map"] = "map",
        ["Picture"] = "picture",
        ["Source"] = "source",
        ["Track"] = "track",
        ["Video"] = "video",

        // Tabular
        ["Caption"] = "caption",
        ["Col"] = "col",
        ["Colgroup"] = "colgroup",
        ["Table"] = "table",
        ["Tbody"] = "tbody",
        ["Td"] = "td",
        ["Tfoot"] = "tfoot",
        ["Th"] = "th",
        ["Thead"] = "thead",
        ["Tr"] = "tr",

        // Forms
        ["Button"] = "button",
        ["Datalist"] = "datalist",
        ["Fieldset"] = "fieldset",
        ["Form"] = "form",
        ["Input"] = "input",
        ["Label"] = "label",
        ["Legend"] = "legend",
        ["Meter"] = "meter",
        ["Optgroup"] = "optgroup",
        ["Option"] = "option",
        ["Output"] = "output",
        ["Progress"] = "progress",
        ["Select"] = "select",
        ["Selectedcontent"] = "selectedcontent",
        ["Textarea"] = "textarea",

        // Interactive
        ["Details"] = "details",
        ["Dialog"] = "dialog",
        ["Summary"] = "summary",
    };

    /// <summary>
    /// The curated tags as a set, derived from <see cref="CuratedTags"/>. Declared after that table
    /// because static field initializers run in declaration order; moving this above it yields an empty
    /// set. <c>KnownSymbolsSyncTests</c> holds it against the name table in both count and content.
    /// </summary>
    private static readonly HashSet<string> CuratedTagValues =
        new(CuratedTags.Values, System.StringComparer.Ordinal);

    /// <summary>
    /// Whether <paramref name="tag"/> is one of the curated element tags. Used by the #140 fold
    /// predicate as its allow-list: a tag outside this set (and outside <see cref="VoidTags"/>) reaches
    /// a <c>Body</c> only through <c>Element("…")</c>, and folding it would mean serializing markup for
    /// a tag whose HTML text interpretation the compiler has not vetted.
    /// </summary>
    /// <remarks>
    /// Static and ordinal, for the same reasons as <see cref="IsVoidTag"/>: the curated table is the
    /// compiler's own and needs nothing resolved out of a compilation, and every curated tag is
    /// lowercase, so <c>Element("DIV")</c> is deliberately not curated.
    /// </remarks>
    public static bool IsCuratedTag(string tag) => CuratedTagValues.Contains(tag);

    /// <summary>The curated element tags, for <c>KnownSymbolsSyncTests</c>.</summary>
    public static IReadOnlyCollection<string> CuratedElementTags => CuratedTagValues;

    /// <summary>
    /// The HTML Living Standard's void elements, the tags that have no closing tag and therefore cannot
    /// carry children. Keyed on the tag rather than the helper name so one table covers both surface
    /// paths: a curated helper resolves through <see cref="ElementTags"/> and an <c>Element</c> call
    /// carries a non-empty constant tag (guaranteed by BCF3009), and both reduce to a tag string before
    /// the check. Two tables, one per path, would agree only by coincidence.
    /// </summary>
    /// <remarks>
    /// Three of the thirteen (<c>base</c>, <c>link</c>, <c>meta</c>) have no curated helper, being
    /// exclusion group 1 in <c>DESIGN.md</c> §4.1, and are reachable only through <c>Element</c>. They
    /// belong here all the same: the reason they are excluded is that writing them childless in a
    /// <c>Body</c> is silently inert, which says nothing about giving them children.
    /// <c>KnownSymbolsSyncTests</c> holds this list against an independent transcription and against the
    /// curated and excluded tables, since this copy is otherwise unguarded.
    /// </remarks>
    private static readonly HashSet<string> VoidTagSet = new(System.StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
    };

    /// <summary>Whether <paramref name="tag"/> is a void element, so children on it are BCF3016.</summary>
    /// <remarks>
    /// Static, unlike <see cref="ClassifySurfaceMethod"/>: the answer is a property of the HTML standard
    /// and not of the referenced runtime, so there is nothing to resolve out of a compilation. Ordinal, like
    /// every other tag comparison in the compiler: a curated helper's tag comes from
    /// <see cref="CuratedTags"/> already lowercase, and an <c>Element</c> tag is emitted as written, so
    /// <c>Element("IMG")</c> renders <c>&lt;IMG&gt;</c> and is deliberately not this check's business.
    /// </remarks>
    public static bool IsVoidTag(string tag) => VoidTagSet.Contains(tag);

    /// <summary>The void tags, for <c>KnownSymbolsSyncTests</c> to hold against its own transcription.</summary>
    public static IReadOnlyCollection<string> VoidTags => VoidTagSet;

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
    /// its reduced symbol, which no map contains.
    /// </remarks>
    public static ISymbol Normalize(IPropertySymbol property) => property.OriginalDefinition;

    /// <summary>
    /// The names of every decoration the referenced runtime declares, for
    /// <see cref="DeclaresDecorationNamed"/>.
    /// </summary>
    private readonly HashSet<string> _decorationNames;

    /// <summary>
    /// Whether the referenced runtime's <c>Decorations</c> type declares a decoration spelled
    /// <paramref name="name"/>: <c>Class</c>, a named attribute shortcut, an event shortcut, <c>Attr</c>,
    /// <c>On</c>, or <c>Bind</c>.
    /// </summary>
    /// <remarks>
    /// The names are read off the symbols resolved out of the referenced runtime assembly rather than from
    /// a literal table, so a user-defined <c>Some.BlazorCodeFirst.Decorations</c> contributes none of them and
    /// a runtime that renames a decoration cannot leave a stale spelling behind here.
    /// <para>
    /// It is nevertheless only a name test, and is <em>never</em> sufficient on its own: any type may
    /// declare a <c>Class</c> or a <c>Title</c>. Every caller must pair it with symbol-identity checks on
    /// the types involved; it exists to narrow those, not to replace them. Compare
    /// <c>RenderMutationAnalyzer</c>, which likewise tests a name and then anchors it by resolving the
    /// containing type and its namespace.
    /// </para>
    /// </remarks>
    public bool DeclaresDecorationNamed(string name) => _decorationNames.Contains(name);

    /// <summary>
    /// Curated element helper property → HTML tag name. Keyed by <see cref="ISymbol"/> rather than
    /// <see cref="IPropertySymbol"/> only because every consumer compares through
    /// <see cref="SymbolEqualityComparer"/>; every key is an <see cref="IPropertySymbol"/>.
    /// </summary>
    public IReadOnlyDictionary<ISymbol, string> ElementTags { get; }

    /// <summary>Named attribute shortcut decoration method → attribute name.</summary>
    public IReadOnlyDictionary<ISymbol, string> AttributeShortcuts { get; }

    /// <summary>Named event shortcut decoration method (e.g. <c>OnClick</c> overloads) → HTML event name.</summary>
    public IReadOnlyDictionary<ISymbol, string> EventShortcuts { get; }

    /// <summary>
    /// The classification table <see cref="ClassifySurfaceMethod"/> answers from, keyed by
    /// <see cref="Normalize(IMethodSymbol)"/>, for <c>KnownSymbolsSyncTests</c> to hold against what the
    /// referenced runtime declares. Nothing in the compiler reads it directly; ask
    /// <see cref="ClassifySurfaceMethod"/>, which normalizes the method first.
    /// </summary>
    public IReadOnlyDictionary<ISymbol, SurfaceMethodKind> SurfaceMethods => _surfaceMethods;

    /// <summary>Resolved symbol for <c>BlazorCodeFirst.Html.ForEach&lt;T&gt;(...)</c>, or null.</summary>
    /// <remarks>
    /// Kept as a symbol, where the other structural <c>Html</c> members are only rows in
    /// <see cref="SurfaceMethods"/>, because the failure scanner looks this one up <em>by name in scope</em>
    /// to recover a call whose own symbol did not resolve, and a name lookup needs the name.
    /// </remarks>
    public IMethodSymbol? HtmlForEach { get; }

    /// <summary>
    /// Resolved symbol for <c>BlazorCodeFirst.Html.Component&lt;T&gt;()</c>, the only component syntax:
    /// children arrive through <see cref="ComponentIndexer"/>, not through an overload. Null if unavailable.
    /// </summary>
    /// <remarks>
    /// Kept as a symbol for the reason <see cref="HtmlForEach"/>'s remarks give, differently applied:
    /// <c>UnresolvedComponentTypeScanner</c> sweeps every invocation under a failed body and needs
    /// something to compare a candidate symbol against before any classification is asked for.
    /// </remarks>
    public IMethodSymbol? HtmlComponent { get; }

    private KnownSymbols(INamedTypeSymbol htmlType, Compilation compilation)
    {
        ViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.View");
        ComposableAttributeType =
            htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ComposableAttribute");
        ComponentViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ComponentView`1");
        ElementBuilderType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ElementBuilder");

        // GetTypeByMetadataName answers null for an *ambiguous* type as well as a missing one, two
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
        RenderFragmentGenericType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.RenderFragment`1");
        EventCallbackType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.EventCallback`1");
        ExpressionType = compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
        FuncType = compilation.GetTypeByMetadataName("System.Func`1");

        var funcWithArgumentType = compilation.GetTypeByMetadataName("System.Func`2");
        _surfaceMethods = new Dictionary<ISymbol, SurfaceMethodKind>(SymbolEqualityComparer.Default);

        if (ComponentViewType is not null)
        {
            foreach (var member in ComponentViewType.GetMembers())
            {
                if (member is not IMethodSymbol method)
                    continue;

                var kind = ClassifyComponentParameterDefinition(
                    method,
                    ComponentViewType,
                    ViewType,
                    RenderFragmentType,
                    RenderFragmentGenericType,
                    funcWithArgumentType);
                if (kind != SurfaceMethodKind.None)
                    _surfaceMethods[Normalize(method)] = kind;
            }

            // All three overloads, which differ only in their setter parameter; the analyzer reads the
            // setter's own type to tell the synchronous one from the asynchronous one, so nothing here
            // has to discriminate them. The name gate above answers None for every one of them, so this
            // registration cannot overwrite a parameter-syntax classification.
            foreach (var member in ComponentViewType.GetMembers("Bind"))
            {
                if (member is IMethodSymbol bindMethod)
                    _surfaceMethods[Normalize(bindMethod)] = SurfaceMethodKind.ComponentBind;
            }
        }

        var decorationsType =
            htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.Decorations");

        var attributeShortcuts = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var eventShortcuts = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        _decorationNames = new HashSet<string>(System.StringComparer.Ordinal);
        if (decorationsType is not null)
        {
            // A decoration is defined by its receiver, not by its name: Decorations declares extension
            // methods on ElementBuilder, and that is what makes .Class/.Attr/.On element decorations
            // rather than members that merely share a name. Capturing by name alone would admit a future
            // overload on another receiver, Attr(this ComponentView<T>, string, string), say, into these
            // sets, where IsDecorationMethod would treat it as an element decoration with nothing to
            // notice. ClassMethod showed the same defect more loudly: a single slot taking whichever
            // two-parameter overload GetMembers returned last.
            //
            // When ElementBuilderType is unavailable the test is skipped rather than failed. Unlike the
            // ambiguous-type scenario above, this lookup (htmlType.ContainingAssembly.GetTypeByMetadataName)
            // is scoped to a single assembly, so null here means that assembly does not declare
            // BlazorCodeFirst.ElementBuilder under that name, not a cross-assembly ambiguity. Rejecting every
            // candidate would still empty these sets and silently disable BCF3008 for every decoration, a
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
                SurfaceMethodKind kind;
                switch (method.Name)
                {
                    // Receiver plus second parameter fully determines (ElementBuilder, string), so this
                    // single classification cannot take an arbitrary overload. Decorations declares exactly
                    // one. Not a list pattern: this project targets netstandard2.0 without a
                    // System.Index/Range polyfill, and the list-pattern lowering requires those types even
                    // without a slice.
                    case "Class" when method.Parameters.Length == 2
                        && method.Parameters[1].Type.SpecialType == SpecialType.System_String:
                        kind = SurfaceMethodKind.Class;
                        break;
                    case "OnClick":
                        // Both overloads map to "onclick"; the analyzer's decoration branch dispatches
                        // on the classification and reads the name out of EventShortcuts, so no separate
                        // first-overload symbol is retained.
                        eventShortcuts[key] = "onclick";
                        kind = SurfaceMethodKind.EventShortcut;
                        break;
                    case "On": kind = SurfaceMethodKind.On; break;               // all four overloads
                    case "Attr": kind = SurfaceMethodKind.Attr; break;
                    // An ordinary classification beside On and Attr, and read the same way: the decoration
                    // arm in RenderExpressionAnalyzer dispatches on it to recognize a binding, and the name
                    // folded out of it below is what lets DeclaresDecorationNamed report a misplaced
                    // .Bind(…) as BCF3008 rather than BCF1003.
                    case "Bind": kind = SurfaceMethodKind.Bind; break;           // all six overloads
                    default:
                        if (!AttributeShortcutNames.TryGetValue(method.Name, out var attr))
                            continue;

                        attributeShortcuts[key] = attr;
                        kind = SurfaceMethodKind.AttributeShortcut;
                        break;
                }

                _surfaceMethods[key] = kind;
                _decorationNames.Add(method.Name);
            }
        }
        AttributeShortcuts = attributeShortcuts;
        EventShortcuts = eventShortcuts;

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

            SurfaceMethodKind kind;
            switch (method.Name)
            {
                // Element(string tag) returns an ElementBuilder and is the only arity: children are written
                // in brackets on that builder rather than passed to a second overload.
                case "Element" when method.Parameters.Length == 1:
                    kind = SurfaceMethodKind.Element;
                    break;
                case "If" when method.Parameters.Length == 3:
                    kind = SurfaceMethodKind.If;
                    break;
                case "ForEach" when method.Parameters.Length == 3 && method.Arity == 1:
                    HtmlForEach = method;
                    kind = SurfaceMethodKind.ForEach;
                    break;
                case "Component" when method.Arity == 1 && method.Parameters.Length == 0:
                    HtmlComponent = method;
                    kind = SurfaceMethodKind.Component;
                    break;
                case "Raw" when method.Parameters.Length == 1:
                    kind = SurfaceMethodKind.Raw;
                    break;
                case "Fragment" when method.Parameters.Length == 1:
                    kind = SurfaceMethodKind.Fragment;
                    break;
                default:
                    continue;
            }

            _surfaceMethods[Normalize(method)] = kind;
        }
        ElementTags = elementTags;
    }

    /// <summary>
    /// Which method of the design-time surface <paramref name="method"/> is, in one lookup against the
    /// table built at construction from the referenced runtime's own declarations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single place that answers this question, for the reason <see cref="SurfaceMethodKind"/>'s
    /// remarks record. There is no null guard on an absent known symbol anywhere in it: a member the
    /// runtime does not declare simply has no row, so it answers <see cref="SurfaceMethodKind.None"/>,
    /// which is what every caller already does with an unrecognized method.
    /// </para>
    /// <para>
    /// The map is compilation-local transient state; no symbol crosses into an incremental model.
    /// </para>
    /// </remarks>
    public SurfaceMethodKind ClassifySurfaceMethod(IMethodSymbol method) =>
        _surfaceMethods.TryGetValue(Normalize(method), out var kind) ? kind : SurfaceMethodKind.None;

    private static SurfaceMethodKind ClassifyComponentParameterDefinition(
        IMethodSymbol method,
        INamedTypeSymbol componentViewType,
        INamedTypeSymbol? viewType,
        INamedTypeSymbol? renderFragmentType,
        INamedTypeSymbol? renderFragmentGenericType,
        INamedTypeSymbol? funcWithArgumentType)
    {
        if (funcWithArgumentType is null
            || method.IsStatic
            || method.Parameters.Length != 2
            || method.ContainingType.TypeArguments.Length != 1
            || method.ReturnType is not INamedTypeSymbol { TypeArguments.Length: 1 } returnType
            || !SymbolEqualityComparer.Default.Equals(returnType.OriginalDefinition, componentViewType)
            || !SymbolEqualityComparer.Default.Equals(
                returnType.TypeArguments[0], method.ContainingType.TypeArguments[0])
            || method.Parameters[0].Type is not INamedTypeSymbol { TypeArguments.Length: 2 } selector
            || !SymbolEqualityComparer.Default.Equals(selector.OriginalDefinition, funcWithArgumentType)
            || !SymbolEqualityComparer.Default.Equals(
                selector.TypeArguments[0], method.ContainingType.TypeArguments[0]))
        {
            return SurfaceMethodKind.None;
        }

        var selectedType = selector.TypeArguments[1];
        if (method.Name == "Param")
        {
            if (method.Arity == 1
                && SymbolEqualityComparer.Default.Equals(selectedType, method.TypeParameters[0])
                && SymbolEqualityComparer.Default.Equals(
                    method.Parameters[1].Type, method.TypeParameters[0]))
            {
                return SurfaceMethodKind.ScalarParam;
            }

            if (method.Arity == 0
                && renderFragmentType is not null
                && viewType is not null
                && SymbolEqualityComparer.Default.Equals(selectedType, renderFragmentType)
                && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, viewType))
            {
                return SurfaceMethodKind.FragmentParam;
            }

            return SurfaceMethodKind.None;
        }

        if (method.Name != "Template"
            || method.Arity != 1
            || renderFragmentGenericType is null
            || viewType is null
            || selectedType is not INamedTypeSymbol { TypeArguments.Length: 1 } genericFragment
            || !SymbolEqualityComparer.Default.Equals(
                genericFragment.OriginalDefinition, renderFragmentGenericType)
            || !SymbolEqualityComparer.Default.Equals(
                genericFragment.TypeArguments[0], method.TypeParameters[0]))
        {
            return SurfaceMethodKind.None;
        }

        if (SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, viewType))
            return SurfaceMethodKind.GenericTemplateIgnored;

        if (method.Parameters[1].Type is INamedTypeSymbol { TypeArguments.Length: 2 } content
            && SymbolEqualityComparer.Default.Equals(content.OriginalDefinition, funcWithArgumentType)
            && SymbolEqualityComparer.Default.Equals(content.TypeArguments[0], method.TypeParameters[0])
            && SymbolEqualityComparer.Default.Equals(content.TypeArguments[1], viewType))
        {
            return SurfaceMethodKind.GenericTemplateContextual;
        }

        return SurfaceMethodKind.None;
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
    /// channel. Compare <c>RenderMutationAnalyzer</c>, which anchors <c>BlazorCodeFirst.Decorations</c> to the
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

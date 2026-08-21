using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Analysis;

/// <summary>
/// Resolved symbol references for the <c>BlazorCodeFirst.Html</c> design-time syntax and their surrounding
/// types so that expression analysis can compare symbols by identity rather than by name.
/// </summary>
/// <remarks>
/// <see cref="TryCreate"/> keys its own small bounded cache on the <see cref="Compilation"/> instance
/// itself (#514), so within one compilation every caller shares one build; it is still never stored in
/// the cached incremental pipeline, and its symbols are only ever compared within the compilation they
/// came from, never across compilations. It therefore needs no value equality of its own.
/// </remarks>
internal sealed class KnownSymbols
{
    /// <summary>Resolved symbol for <c>BlazorCodeFirst.View</c>, or <see langword="null"/> if unavailable.</summary>
    public INamedTypeSymbol? ViewType { get; }

    /// <summary>Resolved symbol for <c>BlazorCodeFirst.ViewPartAttribute</c>, or <see langword="null"/> if unavailable.</summary>
    public INamedTypeSymbol? ViewPartAttributeType { get; }

    /// <summary>Resolved unbound generic <c>BlazorCodeFirst.ComponentView&lt;T&gt;</c>, or null.</summary>
    public INamedTypeSymbol? ComponentViewType { get; }

    /// <summary>
    /// Resolved symbol for <c>BlazorCodeFirst.ElementView</c>, or <see langword="null"/>, which is the
    /// normal case against a runtime that has not adopted the bracket surface.
    /// </summary>
    /// <remarks>
    /// Every consumer must guard on this being non-null before comparing against it.
    /// <c>SymbolEqualityComparer.Default.Equals(x, null)</c> answers <see langword="true"/> for a null
    /// <c>x</c>, so an unguarded comparison would classify an unrelated indexer, <c>_dict["k"]</c>, as an
    /// element.
    /// </remarks>
    public INamedTypeSymbol? ElementViewType { get; }

    /// <summary>
    /// Resolved symbol for <c>ElementView</c>'s <c>params ReadOnlySpan&lt;View&gt;</c> indexer, which is
    /// how children are written on the bracket surface, or null.
    /// </summary>
    public IPropertySymbol? ElementIndexer { get; }

    /// <summary>
    /// Resolved symbol for <c>ComponentView&lt;T&gt;</c>'s <c>params ReadOnlySpan&lt;View&gt;</c> indexer,
    /// which is the one channel child content reaches a component through, or null.
    /// </summary>
    public IPropertySymbol? ComponentIndexer { get; }

    /// <summary>
    /// Resolved symbol for <c>BlazorCodeFirst.SlotView</c>, the return type of a <c>[ViewPart]</c> part
    /// that takes caller-supplied content (#176), or <see langword="null"/> against a runtime that predates
    /// it.
    /// </summary>
    /// <remarks>
    /// Guard on this being non-null before comparing against it, for the reason
    /// <see cref="ElementViewType"/>'s remarks give: <c>SymbolEqualityComparer.Default.Equals(x, null)</c>
    /// answers <see langword="true"/> for a null <c>x</c>, so an unguarded comparison would read an
    /// unrelated return type as a content-taking part.
    /// </remarks>
    public INamedTypeSymbol? SlotViewType { get; }

    /// <summary>
    /// Resolved symbol for <c>SlotView</c>'s <c>params ReadOnlySpan&lt;View&gt;</c> indexer, which is the
    /// one channel content reaches a <c>[ViewPart]</c> part through, or null.
    /// </summary>
    public IPropertySymbol? ContentIndexer { get; }

    /// <summary>
    /// Resolved symbol for <c>BlazorCodeFirst.Html.Slot</c>, the marker naming where a content-taking part
    /// places its caller's content, or null.
    /// </summary>
    /// <remarks>
    /// Kept as a symbol rather than as a row in <see cref="ElementTags"/> or
    /// <see cref="SurfaceMethods"/> because it is neither an element nor a method: it is a hole, and the
    /// analyzer has to answer "is this identifier that hole" by identity before it can look up the ordinal
    /// the enclosing definition bound it at.
    /// </remarks>
    public IPropertySymbol? SlotProperty { get; }

    /// <summary>Resolved <c>Microsoft.AspNetCore.Components.ParameterAttribute</c>, or null.</summary>
    public INamedTypeSymbol? ParameterAttributeType { get; }

    /// <summary>Resolved symbol for <c>Microsoft.AspNetCore.Components.RenderFragment</c>, or null.</summary>
    public INamedTypeSymbol? RenderFragmentType { get; }

    /// <summary>Resolved unbound generic <c>Microsoft.AspNetCore.Components.RenderFragment&lt;T&gt;</c>, or null.</summary>
    public INamedTypeSymbol? RenderFragmentGenericType { get; }

    /// <summary>
    /// Resolved <c>System.Linq.Enumerable.Select&lt;TSource, TResult&gt;(IEnumerable&lt;TSource&gt;,
    /// Func&lt;TSource, TResult&gt;)</c>, the one projection a spliced child list folds (#172), or null.
    /// </summary>
    /// <remarks>
    /// Degrades the same way every other lookup here does. With this null the splice classifies nothing
    /// and lands on BCF1003, which is where a spread sat before #172.
    /// </remarks>
    public IMethodSymbol? EnumerableSelect { get; }

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

    /// <summary>
    /// Resolved <c>System.EventArgs</c>, the base every event argument type derives from and the bound the
    /// <c>.On&lt;TArgs&gt;</c> decoration declares, or null.
    /// </summary>
    /// <remarks>
    /// Guard on this being non-null before comparing against it, for the reason
    /// <see cref="ElementViewType"/>'s remarks give.
    /// </remarks>
    public INamedTypeSymbol? EventArgsType { get; }

    /// <summary>
    /// Resolved <c>Microsoft.AspNetCore.Components.RenderModeAttribute</c>, the base every declaration-form
    /// render mode attribute derives from, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read only by BCF3034, which asks whether a component's own declaration fixes its render mode. Guard
    /// on this being non-null before comparing against it, for the reason
    /// <see cref="ElementViewType"/>'s remarks give — though a compilation able to spell
    /// <c>.RenderMode</c> can always see it, since the assembly declaring <c>ComponentView&lt;T&gt;</c>
    /// references the one declaring this.
    /// </para>
    /// <para>
    /// Resolved lazily, for the reason <see cref="_formatBindableTypes"/> gives about its own lookup: this
    /// type's constructor runs once per <c>[ViewPart]</c> and once per component, and
    /// <c>GetTypeByMetadataName</c> parses the name and consults every referenced assembly to detect
    /// ambiguity. A <c>.RenderMode</c> is rarer than a <c>.Bind</c> carrying a format, so hoisting this
    /// would charge every component for a lookup almost none of them reach.
    /// </para>
    /// </remarks>
    public INamedTypeSymbol? RenderModeAttributeType => _renderModeAttributeType.Value;

    private readonly System.Lazy<INamedTypeSymbol?> _renderModeAttributeType;

    /// <summary>
    /// Resolved <c>Microsoft.AspNetCore.Components.EventCallbackFactory</c>, the type
    /// <c>EventCallback.Factory</c> hands back, read only through
    /// <see cref="IsEventCallbackFactoryMethod"/>. Lazy for the reason
    /// <see cref="_renderModeAttributeType"/> gives.
    /// </summary>
    private readonly System.Lazy<INamedTypeSymbol?> _eventCallbackFactoryType;

    private readonly Dictionary<ISymbol, SurfaceMethodKind> _surfaceMethods;

    /// <summary>
    /// The <c>ComponentView&lt;T&gt;</c> members whose name decides their classification, rather than the
    /// parameter syntax <see cref="ClassifyComponentParameterDefinition"/> reads. Every overload of a name
    /// lands on its row.
    /// </summary>
    /// <remarks>
    /// A table and not a chain of loops, so a channel added here is one row rather than a copied loop that
    /// could pair a name with the wrong kind. The parameter-syntax channels (<c>.Param</c>,
    /// <c>.Template</c>) are deliberately absent: their names carry no meaning, which is the distinction
    /// this table draws.
    /// </remarks>
    private static readonly Dictionary<string, SurfaceMethodKind> ComponentBuilderChannels =
        new(System.StringComparer.Ordinal)
        {
            ["Bind"] = SurfaceMethodKind.ComponentBind,
            ["Key"] = SurfaceMethodKind.ComponentKey,
            ["RenderMode"] = SurfaceMethodKind.ComponentRenderMode,
            ["Ref"] = SurfaceMethodKind.ComponentRef,
            // Declared as ComponentView<T> members, not Decorations extensions (#314) — see
            // ComponentView.Class's remarks for why. Members, so this table is what recognizes them;
            // there is no second receiver for the Decorations-extension loop below to admit.
            ["Class"] = SurfaceMethodKind.ComponentClass,
            ["Attr"] = SurfaceMethodKind.ComponentAttr,
        };

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

    /// <summary>Authoritative <c>Svg</c> element helper name → tag table (#319 / #534), independent of
    /// <see cref="CuratedTags"/>. Runtime helper declarations are kept in sync by
    /// <c>KnownSymbolsSyncTests</c>.</summary>
    /// <remarks>
    /// Transcribed from SVG2's own element index (<c>https://www.w3.org/TR/SVG2/eltindex.html</c>, 69
    /// elements). The naming rule is <see cref="CuratedTags"/>'s own — capitalize only the tag's first
    /// letter — but unlike <see cref="CuratedTags"/> the rest of a tag's casing is preserved rather than
    /// asserted lowercase, since SVG tag names are not all-lowercase (<c>clipPath</c>,
    /// <c>linearGradient</c>, <c>feGaussianBlur</c>, ...). <c>["Root"] = "svg"</c> is the one entry that
    /// breaks the rule's bijection: <c>Svg.Svg</c> is CS0542 (member name equals enclosing type name), so
    /// the root element alone takes an irregular name.
    /// </remarks>
    private static readonly Dictionary<string, string> SvgTags = new(System.StringComparer.Ordinal)
    {
        ["Root"] = "svg",

        ["A"] = "a",
        ["Animate"] = "animate",
        ["AnimateMotion"] = "animateMotion",
        ["AnimateTransform"] = "animateTransform",
        ["Audio"] = "audio",
        ["Canvas"] = "canvas",
        ["Circle"] = "circle",
        ["ClipPath"] = "clipPath",
        ["Defs"] = "defs",
        ["Desc"] = "desc",
        ["Discard"] = "discard",
        ["Ellipse"] = "ellipse",
        ["FeBlend"] = "feBlend",
        ["FeColorMatrix"] = "feColorMatrix",
        ["FeComponentTransfer"] = "feComponentTransfer",
        ["FeComposite"] = "feComposite",
        ["FeConvolveMatrix"] = "feConvolveMatrix",
        ["FeDiffuseLighting"] = "feDiffuseLighting",
        ["FeDisplacementMap"] = "feDisplacementMap",
        ["FeDistantLight"] = "feDistantLight",
        ["FeDropShadow"] = "feDropShadow",
        ["FeFlood"] = "feFlood",
        ["FeFuncA"] = "feFuncA",
        ["FeFuncB"] = "feFuncB",
        ["FeFuncG"] = "feFuncG",
        ["FeFuncR"] = "feFuncR",
        ["FeGaussianBlur"] = "feGaussianBlur",
        ["FeImage"] = "feImage",
        ["FeMerge"] = "feMerge",
        ["FeMergeNode"] = "feMergeNode",
        ["FeMorphology"] = "feMorphology",
        ["FeOffset"] = "feOffset",
        ["FePointLight"] = "fePointLight",
        ["FeSpecularLighting"] = "feSpecularLighting",
        ["FeSpotLight"] = "feSpotLight",
        ["FeTile"] = "feTile",
        ["FeTurbulence"] = "feTurbulence",
        ["Filter"] = "filter",
        ["ForeignObject"] = "foreignObject",
        ["G"] = "g",
        ["Iframe"] = "iframe",
        ["Image"] = "image",
        ["Line"] = "line",
        ["LinearGradient"] = "linearGradient",
        ["Marker"] = "marker",
        ["Mask"] = "mask",
        ["Metadata"] = "metadata",
        ["Mpath"] = "mpath",
        ["Path"] = "path",
        ["Pattern"] = "pattern",
        ["Polygon"] = "polygon",
        ["Polyline"] = "polyline",
        ["RadialGradient"] = "radialGradient",
        ["Rect"] = "rect",
        ["Script"] = "script",
        ["Set"] = "set",
        ["Stop"] = "stop",
        ["Style"] = "style",
        ["Switch"] = "switch",
        ["Symbol"] = "symbol",
        ["Text"] = "text",
        ["TextPath"] = "textPath",
        ["Title"] = "title",
        ["Tspan"] = "tspan",
        ["Unknown"] = "unknown",
        ["Use"] = "use",
        ["Video"] = "video",
        ["View"] = "view",
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
    /// Whether <paramref name="name"/> is spelled like one of the curated element helpers, the key side of
    /// <see cref="CuratedTags"/> where <see cref="IsCuratedTag"/> asks about the value side.
    /// </summary>
    /// <remarks>
    /// Static and ordinal for the reasons <see cref="IsCuratedTag"/> gives, and a <em>syntactic</em> test
    /// like <see cref="DeclaresDecorationNamed"/>: it says only that an identifier is spelled like a helper,
    /// never that it is one. <c>ShadowedElementHelperScanner</c> uses it as the conjunct that asks nothing of
    /// the semantic model, and pairs it with symbol identity against <see cref="ElementTags"/>, which is what
    /// decides whether the name actually reached the helper.
    /// </remarks>
    public static bool IsCuratedHelperName(string name) => CuratedTags.ContainsKey(name);

    /// <summary>
    /// Whether <paramref name="name"/> is spelled like one of the <c>Svg</c> element helpers, the key side
    /// of <see cref="SvgTags"/>.
    /// </summary>
    /// <remarks>
    /// The <c>Svg</c>-side counterpart of <see cref="IsCuratedHelperName"/>, read by
    /// <c>ShadowedElementHelperScanner</c> as the second half of its syntactic prefilter: <c>Html</c>
    /// and <c>Svg</c> share several tag names (<c>a</c>, <c>audio</c>, <c>canvas</c>, <c>iframe</c>,
    /// <c>video</c>), so a shadowed <c>Circle</c> or a shadowed <c>Audio</c> reached through
    /// <c>using static Svg;</c> both need reporting, not only the <c>Html</c> spelling.
    /// </remarks>
    public static bool IsCuratedSvgHelperName(string name) => SvgTags.ContainsKey(name);

    /// <summary>The <see cref="SvgTags"/> tags, for <c>KnownSymbolsSyncTests</c>.</summary>
    public static IReadOnlyCollection<string> CuratedSvgElementTags => SvgTags.Values;

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

    /// <summary>
    /// Whether <paramref name="tag"/> is spelled like a name an element can have: an ASCII letter,
    /// then ASCII letters, digits, hyphen, underscore or period. The condition BCF3009 adds to the
    /// constant one, and a character class rather than a table, so it stays on this side of
    /// <c>DESIGN.md</c> §4.1's boundary.
    /// </summary>
    /// <remarks>
    /// Inside the intersection of what the two render paths accept, not the union. HTML's tokenizer
    /// takes almost anything that is not whitespace, <c>/</c> or <c>&gt;</c>, while the interactive
    /// path goes through <c>createElement</c> and takes the XML <c>Name</c> production. A tag one of
    /// them accepts and the other does not is what makes them disagree rather than merely render
    /// something unusual (#394). The leading character is where they differ most: XML admits a leading
    /// <c>_</c>, but HTML's tokenizer needs an ASCII letter to open a tag at all, so <c>&lt;_x&gt;</c>
    /// prerenders as text and hydrates as an element.
    /// <para>
    /// Deliberately ASCII, for the reason <c>StaticMarkupSerializer.IsCustomElementName</c> gives:
    /// both productions admit a wide range of non-ASCII characters, and refusing an exotic name costs
    /// a spelling nobody writes. Uppercase stays admitted, since both paths lower it to the same
    /// element and <see cref="IsVoidTag"/> already records that <c>Element("IMG")</c> is emitted as
    /// written.
    /// </para>
    /// </remarks>
    public static bool IsValidTagName(string tag)
    {
        if (tag.Length == 0 || !IsAsciiLetter(tag[0]))
            return false;

        for (var index = 1; index < tag.Length; index++)
        {
            if (tag[index] is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                or '-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    /// <summary>Named attribute shortcut method name → attribute name, derived by rule from the HTML Living
    /// Standard's <i>Index — Attributes</i> (ARCHITECTURE.md B.21 revisited, #490); <c>Role</c> is the one
    /// hand-picked exception. The compiler owns this map; runtime declarations are kept in sync by
    /// KnownSymbolsSyncTests.</summary>
    private static readonly Dictionary<string, string> AttributeShortcutNames = new(System.StringComparer.Ordinal)
    {
        // Predate #490 (decided 2026-08-14, #321) and kept first for stability. Six of these seven also
        // satisfy the rule below and are rule-derived like every other entry; only Role is not (it is
        // ARIA-defined, not a row in the standard's own index) and is this table's one hand-picked entry.
        ["Href"] = "href",
        ["Src"] = "src",
        ["Alt"] = "alt",
        ["Id"] = "id",
        ["Type"] = "type",
        ["Title"] = "title",
        ["Role"] = "role",

        // Standard-derived (#490): HTML Living Standard *Index — Attributes*, name verbatim, value
        // determinate as string-or-bool, one attribute frame. Alphabetical by method name.
        ["Abbr"] = "abbr",
        ["Accept"] = "accept",
        ["AcceptCharset"] = "accept-charset",
        ["Accesskey"] = "accesskey",
        ["Action"] = "action",
        ["Allow"] = "allow",
        ["Allowfullscreen"] = "allowfullscreen",
        ["Alpha"] = "alpha",
        ["As"] = "as",
        ["Async"] = "async",
        ["Autocapitalize"] = "autocapitalize",
        ["Autocomplete"] = "autocomplete",
        ["Autocorrect"] = "autocorrect",
        ["Autofocus"] = "autofocus",
        ["Autoplay"] = "autoplay",
        ["Blocking"] = "blocking",
        ["Charset"] = "charset",
        ["Checked"] = "checked",
        ["Cite"] = "cite",
        ["Closedby"] = "closedby",
        ["Color"] = "color",
        ["Colorspace"] = "colorspace",
        ["Cols"] = "cols",
        ["Colspan"] = "colspan",
        ["Command"] = "command",
        ["Commandfor"] = "commandfor",
        ["Content"] = "content",
        ["Contenteditable"] = "contenteditable",
        ["Controls"] = "controls",
        ["Coords"] = "coords",
        ["Crossorigin"] = "crossorigin",
        ["Data"] = "data",
        ["Datetime"] = "datetime",
        ["Decoding"] = "decoding",
        ["Default"] = "default",
        ["Defer"] = "defer",
        ["Dir"] = "dir",
        ["Dirname"] = "dirname",
        ["Disabled"] = "disabled",
        ["Download"] = "download",
        ["Draggable"] = "draggable",
        ["Enctype"] = "enctype",
        ["Enterkeyhint"] = "enterkeyhint",
        ["Fetchpriority"] = "fetchpriority",
        ["For"] = "for",
        ["Form"] = "form",
        ["Formaction"] = "formaction",
        ["Formenctype"] = "formenctype",
        ["Formmethod"] = "formmethod",
        ["Formnovalidate"] = "formnovalidate",
        ["Formtarget"] = "formtarget",
        ["Headers"] = "headers",
        ["Headingoffset"] = "headingoffset",
        ["Headingreset"] = "headingreset",
        ["Height"] = "height",
        ["Hidden"] = "hidden",
        ["High"] = "high",
        ["Hreflang"] = "hreflang",
        ["HttpEquiv"] = "http-equiv",
        ["Imagesizes"] = "imagesizes",
        ["Imagesrcset"] = "imagesrcset",
        ["Inert"] = "inert",
        ["Inputmode"] = "inputmode",
        ["Integrity"] = "integrity",
        ["Is"] = "is",
        ["Ismap"] = "ismap",
        ["Itemid"] = "itemid",
        ["Itemprop"] = "itemprop",
        ["Itemref"] = "itemref",
        ["Itemscope"] = "itemscope",
        ["Itemtype"] = "itemtype",
        ["Kind"] = "kind",
        ["Label"] = "label",
        ["Lang"] = "lang",
        ["List"] = "list",
        ["Loading"] = "loading",
        ["Loop"] = "loop",
        ["Low"] = "low",
        ["Max"] = "max",
        ["Maxlength"] = "maxlength",
        ["Media"] = "media",
        ["Method"] = "method",
        ["Min"] = "min",
        ["Minlength"] = "minlength",
        ["Multiple"] = "multiple",
        ["Muted"] = "muted",
        ["Name"] = "name",
        ["Nomodule"] = "nomodule",
        ["Nonce"] = "nonce",
        ["Novalidate"] = "novalidate",
        ["Open"] = "open",
        ["Optimum"] = "optimum",
        ["Pattern"] = "pattern",
        ["Ping"] = "ping",
        ["Placeholder"] = "placeholder",
        ["Playsinline"] = "playsinline",
        ["Popover"] = "popover",
        ["Popovertarget"] = "popovertarget",
        ["Popovertargetaction"] = "popovertargetaction",
        ["Poster"] = "poster",
        ["Preload"] = "preload",
        ["Readonly"] = "readonly",
        ["Referrerpolicy"] = "referrerpolicy",
        ["Rel"] = "rel",
        ["Required"] = "required",
        ["Reversed"] = "reversed",
        ["Rows"] = "rows",
        ["Rowspan"] = "rowspan",
        ["Sandbox"] = "sandbox",
        ["Scope"] = "scope",
        ["Selected"] = "selected",
        ["Shadowrootclonable"] = "shadowrootclonable",
        ["Shadowrootcustomelementregistry"] = "shadowrootcustomelementregistry",
        ["Shadowrootdelegatesfocus"] = "shadowrootdelegatesfocus",
        ["Shadowrootmode"] = "shadowrootmode",
        ["Shadowrootserializable"] = "shadowrootserializable",
        ["Shadowrootslotassignment"] = "shadowrootslotassignment",
        ["Shape"] = "shape",
        ["Size"] = "size",
        ["Sizes"] = "sizes",
        ["Slot"] = "slot",
        ["Span"] = "span",
        ["Spellcheck"] = "spellcheck",
        ["Srcdoc"] = "srcdoc",
        ["Srclang"] = "srclang",
        ["Srcset"] = "srcset",
        ["Start"] = "start",
        ["Step"] = "step",
        ["Tabindex"] = "tabindex",
        ["Target"] = "target",
        ["Translate"] = "translate",
        ["Usemap"] = "usemap",
        ["Value"] = "value",
        ["Width"] = "width",
        ["Wrap"] = "wrap",
        ["Writingsuggestions"] = "writingsuggestions",
    };

    /// <summary>Named event shortcut method name → HTML event name.</summary>
    /// <remarks>
    /// A table rather than a <c>case</c> arm in the constructor's member switch, for the reason the
    /// attribute side has always been one: adding a shortcut is a row, and everything that reads the event
    /// side — the decoration arm of <c>RenderExpressionAnalyzer</c>, <see cref="DeclaresDecorationNamed"/>,
    /// and <c>RenderMutationAnalyzer</c>'s deferred-handler exemption — follows from the registration
    /// instead of naming the shortcut again. <c>KnownSymbolsSyncTests</c> holds it against every decoration
    /// the runtime declares in that shape, so a row omitted for a new one is red rather than silent.
    /// <para>
    /// The event name is written out rather than derived from the method name: an event whose HTML spelling
    /// is not the member name minus <c>On</c>, <c>ondblclick</c> for instance, is then an ordinary row and
    /// not a special case.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> EventShortcutNames = new(System.StringComparer.Ordinal)
    {
        ["OnClick"] = "onclick",
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
    /// Whether <paramref name="method"/> carries an extension method's receiver at ordinal 0, as the
    /// <c>1</c> or <c>0</c> to subtract from a parameter ordinal or from a parameter count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place that decides the receiver skip, beside <see cref="Normalize(IMethodSymbol)"/> because
    /// it adapts the same thing: Roslyn hands one call's method out in more than one spelling and only one
    /// of them carries the receiver. A classic <c>this</c> extension method has it at ordinal 0 unreduced,
    /// while the reduced spelling, an instance method, and a C# 14 extension member all exclude it already
    /// — the last because Roslyn answers <see cref="IMethodSymbol.IsExtensionMethod"/> with
    /// <see langword="false"/> for that declaration form and hangs the receiver off the containing
    /// extension block instead (#203).
    /// </para>
    /// <para>
    /// It is therefore correct on whichever spelling it is handed, and a caller need not normalize first:
    /// on an already-unreduced method it answers 1 exactly where there is a receiver to skip, and on a
    /// reduced one it answers 0, which is what indexing that method's own parameter list wants. That is
    /// what lets a caller reading argument space drop its <c>ReducedFrom ?? method</c> step. A caller that
    /// needs the whole <em>declared</em> list still takes it, because the static spelling writes the
    /// receiver as an argument and is checked against every parameter (#211).
    /// </para>
    /// </remarks>
    public static int ReceiverOffset(IMethodSymbol method) =>
        method is { IsExtensionMethod: true, ReducedFrom: null } ? 1 : 0;

    /// <summary>
    /// Whether <paramref name="method"/> is the projection overload of <c>Enumerable.Select</c>.
    /// </summary>
    /// <remarks>
    /// Asked through <see cref="Normalize(IMethodSymbol)"/> like every other keyed comparison here, which
    /// is what makes a fluent call (resolved to the reduced symbol) and a constructed one (resolved with
    /// real type arguments) both answer. The static spelling would answer too, but no caller reaches it:
    /// both gate on a one-argument member-access call first, and <c>Enumerable.Select(src, f)</c> is
    /// neither.
    /// </remarks>
    public bool IsEnumerableSelect(IMethodSymbol method) =>
        Matches(Normalize(method), EnumerableSelect);

    /// <summary>
    /// Whether <paramref name="method"/> is declared on <c>EventCallbackFactory</c>, the type
    /// <c>EventCallback.Factory</c> hands back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The type rather than a member of it, which is what lets one comparison answer for every overload —
    /// thirteen in <c>Microsoft.AspNetCore.Components</c> 10.0, eleven <c>Create</c> and two
    /// <c>CreateInferred</c>, differing in the callback's shape — without this file enumerating a
    /// framework's overload set. Nothing else is declared there that a delegate can be written into: every
    /// parameter of every one of them is the receiver, the callback, or the value <c>CreateInferred</c>
    /// carries after it. That is also why the comparison is against the type and not against a parameter
    /// position: <c>CreateInferred</c>'s callback is not its last parameter, so the rule the capture
    /// channel uses one file over would answer differently on two members of one type. The caller in
    /// <c>RenderMutationAnalyzer</c> can therefore read this as "the delegate is stored, not invoked"
    /// without naming a parameter role, where the surface's own channels have to name one (#221).
    /// </para>
    /// <para>
    /// <c>CreateBinder</c> is not covered: it is an extension method declared on
    /// <c>EventCallbackFactoryBinderExtensions</c>, so it answers <see langword="false"/> here. Its setter
    /// is as deferred as a callback is, and exempting it is a decision waiting on someone writing one by
    /// hand — the surface's own <c>.Bind</c> is what an author reaches for, and Razor writes the binder
    /// call itself.
    /// </para>
    /// <para>
    /// Answers <see langword="false"/> when the type cannot be resolved, which degrades the way every
    /// other lookup here does.
    /// </para>
    /// </remarks>
    public bool IsEventCallbackFactoryMethod(IMethodSymbol method) =>
        Matches(method.ContainingType, _eventCallbackFactoryType.Value);

    /// <summary>
    /// The projection overload of <c>Enumerable.Select</c>, or <see langword="null"/> when it cannot be
    /// resolved.
    /// </summary>
    /// <remarks>
    /// Both overloads declare two parameters, so the selector's own arity is the discriminator:
    /// <c>Func&lt;TSource, TResult&gt;</c> is the projection, and <c>Func&lt;TSource, int, TResult&gt;</c>
    /// is the indexed form, whose two-parameter lambda has no single iteration variable for a content
    /// template to bind.
    /// </remarks>
    private static IMethodSymbol? FindEnumerableSelect(INamedTypeSymbol? enumerableType)
    {
        if (enumerableType is null)
            return null;

        foreach (var member in enumerableType.GetMembers("Select"))
        {
            if (member is IMethodSymbol { Parameters.Length: 2 } method
                && method.Parameters[1].Type is INamedTypeSymbol { TypeArguments.Length: 2 })
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="parameter"/>'s index in argument space, the receiver excluded, or <c>-1</c> for the
    /// receiver itself, which argument space has no index for.
    /// </summary>
    /// <remarks>
    /// The parameter-shaped form of <see cref="ReceiverOffset(IMethodSymbol)"/>, for the readers that hold
    /// a parameter rather than the method that declared it — <see cref="BindParameters.IsSetter"/> and
    /// <see cref="EventParameters.IsHandler"/>, both of which are handed one by an operation's
    /// <c>IArgumentOperation.Parameter</c>. A parameter of anything but a method, an indexer's, has no
    /// receiver ahead of it, so its ordinal is already its argument index.
    /// </remarks>
    public static int ArgumentIndex(IParameterSymbol parameter) =>
        parameter.Ordinal
            - (parameter.ContainingSymbol is IMethodSymbol method ? ReceiverOffset(method) : 0);

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
    /// the types involved; it exists to narrow those, not to replace them. It answers a question the
    /// classification cannot — whether a <em>failed</em> call was reaching for a decoration, where there is
    /// no resolved symbol to look up. Every recognition of a call that <em>did</em> resolve goes through
    /// <see cref="ClassifySurfaceMethod"/> instead, <c>RenderMutationAnalyzer</c>'s deferred-handler
    /// exemption included (#194).
    /// </para>
    /// </remarks>
    public bool DeclaresDecorationNamed(string name) => _decorationNames.Contains(name);

    /// <summary>
    /// Curated element helper property → HTML tag name. Keyed by <see cref="ISymbol"/> rather than
    /// <see cref="IPropertySymbol"/> only because every consumer compares through
    /// <see cref="SymbolEqualityComparer"/>; every key is an <see cref="IPropertySymbol"/>.
    /// </summary>
    public IReadOnlyDictionary<ISymbol, string> ElementTags { get; }

    /// <summary>
    /// Curated <c>Svg</c> element helper property → tag name (#319 / #534), keyed the way
    /// <see cref="ElementTags"/> is keyed.
    /// </summary>
    /// <remarks>
    /// A separate dictionary rather than a merge into <see cref="ElementTags"/>: <c>Html</c> and
    /// <c>Svg</c> declare several same-named helpers (<c>A</c>, <c>Audio</c>, <c>Canvas</c>,
    /// <c>Iframe</c>, <c>Video</c>) resolving to different tags, and every test that pins
    /// <see cref="ElementTags"/> to <see cref="CuratedTags"/> — count, bijection, and the exclusion
    /// guard's tag-value check — is written against the <c>Html</c> set alone. Keeping the two
    /// tables apart leaves those assertions untouched; <c>RenderExpressionAnalyzer</c> and
    /// <c>ShadowedElementHelperScanner</c> read both, one after the other.
    /// </remarks>
    public IReadOnlyDictionary<ISymbol, string> SvgElementTags { get; }

    /// <summary>Named attribute shortcut decoration method → attribute name.</summary>
    public IReadOnlyDictionary<ISymbol, string> AttributeShortcuts { get; }

    /// <summary>Named event shortcut decoration method (e.g. <c>OnClick</c> overloads) → HTML event name.</summary>
    public IReadOnlyDictionary<ISymbol, string> EventShortcuts { get; }

    /// <summary>
    /// <c>ComponentView&lt;TComponent&gt;.Id</c>/<c>.Type</c>/<c>.Title</c>/<c>.Role</c>/<c>.Href</c>/
    /// <c>.Src</c>/<c>.Alt</c> (#489) → attribute name. The component-side counterpart of
    /// <see cref="AttributeShortcuts"/>, read the same way by <c>ClassifyComponentAttribute</c>.
    /// </summary>
    public IReadOnlyDictionary<ISymbol, string> ComponentAttributeShortcuts { get; }

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

    /// <summary>
    /// The <c>[EventHandler]</c> registrations the framework ships, event name → argument type, resolved on
    /// first use. Read before <see cref="_sourceEventRegistrations"/>: this table is the canonical one, and a
    /// compilation re-registering a name it already carries does not displace it.
    /// </summary>
    /// <remarks>
    /// Lazy for cost rather than for correctness. A compilation that writes neither a typed
    /// <c>.On&lt;TArgs&gt;</c> nor an event modifier asks nothing of either table, and
    /// <see cref="System.Lazy{T}"/> rather than a null check because
    /// <c>RenderMutationAnalyzer</c> resolves one instance at compilation-start and its per-node callbacks
    /// run concurrently against it.
    /// </remarks>
    private readonly System.Lazy<Dictionary<string, EventRegistration>> _frameworkEventRegistrations;

    /// <summary>
    /// The <c>[EventHandler]</c> registrations declared in the compilation being built, which is how a
    /// custom event is registered at all. Resolved on first use, and only for an event name the framework
    /// table does not answer, so the walk over the compilation's own types is paid by a custom event rather
    /// than by every typed handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not gated on <c>Microsoft.AspNetCore.Components.Web</c>, which
    /// <see cref="_frameworkEventRegistrations"/> is; the constructor's gate says why. So the walk runs in
    /// a compilation without that assembly, where it never used to, and the framework table cannot answer
    /// ahead of it there — every name asked about reaches it (#396).
    /// </para>
    /// <para>
    /// Referenced assemblies are deliberately not swept: #155 could not bound what that costs, so a
    /// registration living in one goes unread and is recorded as residue (<c>ARCHITECTURE.md</c> Appendix A
    /// BCF3028).
    /// </para>
    /// </remarks>
    private readonly System.Lazy<Dictionary<string, EventRegistration>> _sourceEventRegistrations;

    /// <summary>
    /// The value types the framework declares a format-taking <c>CreateBinder</c> overload for, which is
    /// the set BCF3031 admits a <c>.Bind</c> format on.
    /// </summary>
    /// <remarks>
    /// Read from metadata the framework ships, never enumerated here — the criterion <c>DESIGN.md</c> §4.1
    /// states, and the shape the <c>[EventHandler]</c> tables above already use. Lazy for the same reason
    /// they are: a compilation that writes no format asks nothing of it.
    /// <para>
    /// Empty when <c>EventCallbackFactoryBinderExtensions</c> cannot be resolved, in which case
    /// <see cref="AcceptsBindFormat"/> skips the check rather than rejecting everything, as BCF3028 does
    /// with a missing event table. That case is unreachable in practice: the assembly declaring
    /// <c>ElementView</c> references <c>Microsoft.AspNetCore.Components</c>, so a compilation able to
    /// spell <c>.Bind</c> can see this type. It is a defence, not an expected path.
    /// </para>
    /// </remarks>
    private readonly System.Lazy<HashSet<ITypeSymbol>> _formatBindableTypes;

    private KnownSymbols(INamedTypeSymbol htmlType, Compilation compilation)
    {
        ViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.View");
        ViewPartAttributeType =
            htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ViewPartAttribute");
        ComponentViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ComponentView`1");
        ElementViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.ElementView");
        SlotViewType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.SlotView");

        // GetTypeByMetadataName answers null for an *ambiguous* type as well as a missing one, two
        // references both declaring System.ReadOnlySpan<T>, say. Both indexers would then resolve to null and
        // every Div[…] in the compilation would fall through to BCF1003 with nothing naming the cause.
        // ParameterAttributeType and RenderFragmentType degrade the same way, for the same reason.
        var readOnlySpanType = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
        ElementIndexer = FindChildrenIndexer(ElementViewType, ViewType, readOnlySpanType);
        ComponentIndexer = FindChildrenIndexer(ComponentViewType, ViewType, readOnlySpanType);
        ContentIndexer = FindChildrenIndexer(SlotViewType, ViewType, readOnlySpanType);
        ParameterAttributeType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.ParameterAttribute");
        RenderFragmentType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.RenderFragment");
        RenderFragmentGenericType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.RenderFragment`1");
        EventCallbackType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.EventCallback`1");
        ExpressionType = compilation.GetTypeByMetadataName("System.Linq.Expressions.Expression`1");
        EnumerableSelect = FindEnumerableSelect(
            compilation.GetTypeByMetadataName("System.Linq.Enumerable"));
        FuncType = compilation.GetTypeByMetadataName("System.Func`1");
        EventArgsType = compilation.GetTypeByMetadataName("System.EventArgs");

        // Two sources for one table, and each is gated on what it actually reads. EventHandlerAttribute is
        // declared in Microsoft.AspNetCore.Components, which this compiler's own runtime references; the
        // framework's own 96 registrations hang off a class in Microsoft.AspNetCore.Components.Web. So the
        // framework table needs both types and the source table needs only the attribute, and a compilation
        // without Components.Web has no framework mapping to disagree with while still reading an
        // author's own registration.
        //
        // #155 gated both on the framework's, and #396 separated them: what an author declares here is the
        // only thing that gives a custom event name a meaning at all, and skipping it left the diagnostics
        // that consult this table silent in a way indistinguishable from having run. The silence for an
        // event only the framework registers is unchanged.
        var eventHandlerAttributeType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.EventHandlerAttribute");
        var eventHandlersType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.Web.EventHandlers");

        _frameworkEventRegistrations = new System.Lazy<Dictionary<string, EventRegistration>>(() =>
            eventHandlerAttributeType is not null && eventHandlersType is not null
                ? CollectEventRegistrations(eventHandlersType, eventHandlerAttributeType)
                : []);

        _sourceEventRegistrations = new System.Lazy<Dictionary<string, EventRegistration>>(() =>
            eventHandlerAttributeType is not null
                ? CollectEventRegistrations(compilation.Assembly.GlobalNamespace, eventHandlerAttributeType)
                : []);

        // The lookup is inside the lambda, not beside it. This constructor runs once per [ViewPart] and
        // once per component, so a lookup hoisted here would be paid by every one of them — including
        // the great majority that never write a format — for a table only a format reads.
        // GetTypeByMetadataName is not free: it parses the name and consults every referenced assembly
        // to detect ambiguity. Nothing is retained by moving it in, because the sibling lambdas below
        // already close over `compilation` and all three share one display class.
        _renderModeAttributeType = new System.Lazy<INamedTypeSymbol?>(() =>
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.RenderModeAttribute"));

        _eventCallbackFactoryType = new System.Lazy<INamedTypeSymbol?>(() =>
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.EventCallbackFactory"));

        _formatBindableTypes = new System.Lazy<HashSet<ITypeSymbol>>(() =>
            compilation.GetTypeByMetadataName(
                "Microsoft.AspNetCore.Components.EventCallbackFactoryBinderExtensions") is { } binderExtensions
                ? CollectFormatBindableTypes(binderExtensions)
                : []);

        var funcWithArgumentType = compilation.GetTypeByMetadataName("System.Func`2");
        // Read only inside the ComponentViewType member walk below, to recognize the six EventCallback
        // .Param overloads (#492) — locals rather than properties, for the same reason funcWithArgumentType
        // above is one: nothing outside that walk needs them.
        var eventCallbackNonGenericType =
            compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Components.EventCallback");
        var actionType = compilation.GetTypeByMetadataName("System.Action");
        var actionGenericType = compilation.GetTypeByMetadataName("System.Action`1");
        _surfaceMethods = new Dictionary<ISymbol, SurfaceMethodKind>(SymbolEqualityComparer.Default);
        var componentAttributeShortcuts = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);

        if (ComponentViewType is not null)
        {
            // One walk of the type's members, classifying each by name where the name decides and by
            // parameter syntax otherwise, which is the shape the Decorations loop below already uses. The
            // name-keyed channels are read from a table rather than written as a loop apiece: four copies
            // of one five-line loop differing in a string and an enum member gave every future channel its
            // own chance to pair the wrong kind with a name, and nothing fails on that — a mis-registered
            // kind just routes to the wrong classification arm.
            //
            // Every overload of a named channel lands on its row. Bind has three, differing only in their
            // setter parameter, and the analyzer reads the setter's own type to tell the synchronous one
            // from the asynchronous one, so nothing here has to discriminate them. The others have one
            // each today, and a second would be registered rather than silently unclassified.
            foreach (var member in ComponentViewType.GetMembers())
            {
                if (member is not IMethodSymbol method)
                    continue;

                // The name gate wins, and cannot collide with the parameter-syntax classification below:
                // ClassifyComponentParameterDefinition answers None for every one of these names.
                if (ComponentBuilderChannels.TryGetValue(method.Name, out var named))
                {
                    _surfaceMethods[Normalize(method)] = named;
                    continue;
                }

                // Id/Type/Title/Role/Href/Src/Alt (#489): the same seven names as the element-side
                // AttributeShortcutNames table's first seven rows, reused rather than duplicated — this
                // receiver only ever declares those seven, so nothing here can pick up one of #490's
                // later, standard-derived rows by accident. Every shortcut has exactly one parameter
                // (the value; unlike an extension method, there is no receiver to also count), the same
                // arity guard the Decorations loop below applies to its own shortcuts.
                if (method.Parameters.Length == 1
                    && AttributeShortcutNames.TryGetValue(method.Name, out var componentAttrName))
                {
                    componentAttributeShortcuts[Normalize(method)] = componentAttrName;
                    _surfaceMethods[Normalize(method)] = SurfaceMethodKind.ComponentAttributeShortcut;
                    continue;
                }

                var kind = ClassifyComponentParameterDefinition(
                    method,
                    ComponentViewType,
                    ViewType,
                    RenderFragmentType,
                    RenderFragmentGenericType,
                    funcWithArgumentType,
                    EventCallbackType,
                    eventCallbackNonGenericType,
                    actionType,
                    actionGenericType,
                    FuncType);
                if (kind != SurfaceMethodKind.None)
                    _surfaceMethods[Normalize(method)] = kind;
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
            // methods on ElementView, and that is what makes .Class/.Attr/.On element decorations
            // rather than members that merely share a name. Capturing by name alone would admit a future
            // overload on another receiver, Attr(this ComponentView<T>, string, string), say, into these
            // sets, where IsDecorationMethod would treat it as an element decoration with nothing to
            // notice. ClassMethod showed the same defect more loudly: a single slot taking whichever
            // two-parameter overload GetMembers returned last.
            //
            // When ElementViewType is unavailable the test is skipped rather than failed. Unlike the
            // ambiguous-type scenario above, this lookup (htmlType.ContainingAssembly.GetTypeByMetadataName)
            // is scoped to a single assembly, so null here means that assembly does not declare
            // BlazorCodeFirst.ElementView under that name, not a cross-assembly ambiguity. Rejecting every
            // candidate would still empty these sets and silently disable BCF3008 for every decoration, a
            // worse failure than the one prevented, and an invisible one, where the current degradation at
            // least reports BCF1003.
            foreach (var member in decorationsType.GetMembers())
            {
                if (member is not IMethodSymbol { IsExtensionMethod: true } method)
                    continue;

                if (ElementViewType is not null
                    && !(method.Parameters.Length > 0
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, ElementViewType)))
                {
                    continue;
                }

                var key = Normalize(method);
                SurfaceMethodKind kind;
                switch (method.Name)
                {
                    // Receiver plus second parameter fully determines (ElementView, string), so this
                    // single classification cannot take an arbitrary overload. Decorations declares exactly
                    // one. Not a list pattern: this project targets netstandard2.0 without a
                    // System.Index/Range polyfill, and the list-pattern lowering requires those types even
                    // without a slice.
                    case "Class" when method.Parameters.Length == 2
                        && method.Parameters[1].Type.SpecialType == SpecialType.System_String:
                        kind = SurfaceMethodKind.Class;
                        break;
                    case "On": kind = SurfaceMethodKind.On; break;               // all four overloads
                    case "Attr": kind = SurfaceMethodKind.Attr; break;
                    // An ordinary classification beside On and Attr, and read the same way: the decoration
                    // arm in RenderExpressionAnalyzer dispatches on it to recognize a binding, and the name
                    // folded out of it below is what lets DeclaresDecorationNamed report a misplaced
                    // .Bind(…) as BCF3008 rather than BCF1003.
                    case "Bind": kind = SurfaceMethodKind.Bind; break;           // all six overloads
                    // Registered here rather than beside the shortcut tables because it stands for no
                    // attribute name at all: the decoration arm routes on the classification and reads the
                    // key straight off the argument (§2.7(E)).
                    case "Key": kind = SurfaceMethodKind.Key; break;
                    case "Ref": kind = SurfaceMethodKind.Ref; break;
                    // Registered here for the same reason as Key: it stands for no attribute name, and the
                    // decoration arm routes on the classification and reads the form name straight off the
                    // argument (§2.7(E)).
                    case "FormName": kind = SurfaceMethodKind.FormName; break;
                    // Registered here for the same reason as Key/FormName: it stands for no single
                    // attribute name, and the decoration arm routes on the classification and reads
                    // the dictionary expression straight off the argument.
                    case "Attrs": kind = SurfaceMethodKind.AttributesSplat; break;
                    // Both overloads of each land on one row, as the event shortcuts do: the decoration arm
                    // reads the value off the argument list and needs no per-overload symbol. Named here
                    // rather than in a shortcut table because neither stands for an attribute name the
                    // author wrote — the name is built from the event they attach to (#368).
                    case "PreventDefault": kind = SurfaceMethodKind.PreventDefault; break;
                    case "StopPropagation": kind = SurfaceMethodKind.StopPropagation; break;
                    default:
                        // Every shortcut is (receiver, one value), the same shape Class is constrained to
                        // above. A name match alone would register a future overload of a shortcut name
                        // with a different signature too, and the decoration arm would then read firstArg
                        // as the value regardless of what that signature means.
                        if (method.Parameters.Length != 2)
                            continue;

                        // Every overload of a shortcut lands on the same row: .OnClick(Action) and
                        // .OnClick(Func<Task>) both stand for "onclick", and the decoration arm of
                        // RenderExpressionAnalyzer reads the name out of EventShortcuts after dispatching
                        // on the classification, so no per-overload symbol is retained.
                        if (EventShortcutNames.TryGetValue(method.Name, out var eventName))
                        {
                            eventShortcuts[key] = eventName;
                            kind = SurfaceMethodKind.EventShortcut;
                            break;
                        }

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
        ComponentAttributeShortcuts = componentAttributeShortcuts;

        var elementTags = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        foreach (var member in htmlType.GetMembers())
        {
            // A curated helper is a property returning ElementView; children are written in brackets on
            // the indexer that type declares. The return type is checked as well as the name: a property
            // that merely shares a curated name is not an element helper.
            if (member is IPropertySymbol { IsIndexer: false } elementProperty)
            {
                if (ElementViewType is not null
                    && SymbolEqualityComparer.Default.Equals(elementProperty.Type, ElementViewType)
                    && CuratedTags.TryGetValue(elementProperty.Name, out var propertyTag))
                {
                    elementTags[Normalize(elementProperty)] = propertyTag;
                }

                // Slot is a View-typed property, so it cannot collide with an element helper (those return
                // ElementView) and the two arms are disjoint. The return type is checked as well as the
                // name for the same reason it is above: a property that merely shares the name is not the
                // hole.
                if (elementProperty.Name == "Slot"
                    && ViewType is not null
                    && SymbolEqualityComparer.Default.Equals(elementProperty.Type, ViewType))
                {
                    SlotProperty = elementProperty;
                }

                continue;
            }

            if (member is not IMethodSymbol method)
                continue;

            SurfaceMethodKind kind;
            switch (method.Name)
            {
                // Element(string tag) returns an ElementView and is the only arity: children are written
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

        // Absent against a runtime that predates #319 / #534, the same degrade every other type resolved
        // out of htmlType.ContainingAssembly uses: SvgElementTags is then empty and every Svg.* call falls
        // through to the ordinary unresolved-member path.
        var svgType = htmlType.ContainingAssembly.GetTypeByMetadataName("BlazorCodeFirst.Svg");
        var svgElementTags = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        if (svgType is not null)
        {
            foreach (var member in svgType.GetMembers())
            {
                if (member is IPropertySymbol { IsIndexer: false } elementProperty
                    && ElementViewType is not null
                    && SymbolEqualityComparer.Default.Equals(elementProperty.Type, ElementViewType)
                    && SvgTags.TryGetValue(elementProperty.Name, out var propertyTag))
                {
                    svgElementTags[Normalize(elementProperty)] = propertyTag;
                }
            }
        }
        SvgElementTags = svgElementTags;
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

    /// <summary>
    /// Whether <paramref name="property"/> is <c>Html.Slot</c>, the hole naming where a content-taking part
    /// places its caller's content (#176).
    /// </summary>
    /// <remarks>
    /// The three content-surface questions live here for the reason
    /// <see cref="ClassifySurfaceMethod"/> does: each is asked from more than one layer — this one from the
    /// analyzer's property arm and from the declaration's slot count — and each needs the null guard this
    /// class's remarks warn about, <c>SymbolEqualityComparer.Default.Equals(x, null)</c> answering
    /// <see langword="true"/> for a null <c>x</c>. Stated once, they cannot drift apart.
    /// </remarks>
    public bool IsSlot(IPropertySymbol property) =>
        SlotProperty is { } slotProperty
        && SymbolEqualityComparer.Default.Equals(Normalize(property), Normalize(slotProperty));

    /// <summary>
    /// Whether <paramref name="type"/> is <c>View</c>, which in a <c>[ViewPart]</c> parameter position makes
    /// that parameter a content slot rather than a value (#34).
    /// </summary>
    public bool IsContentType(ITypeSymbol type) =>
        ViewType is { } viewType && SymbolEqualityComparer.Default.Equals(type, viewType);

    /// <summary>
    /// Whether <paramref name="method"/> is declared to take caller content, which is exactly whether it
    /// returns <c>SlotView</c> (#176).
    /// </summary>
    public bool TakesContent(IMethodSymbol method) =>
        SlotViewType is { } slotViewType
        && SymbolEqualityComparer.Default.Equals(method.ReturnType, slotViewType);

    /// <summary>
    /// Whether <paramref name="type"/> is one of the design-time inert types — <c>View</c>,
    /// <c>ElementView</c>, <c>SlotView</c>, or any construction of <c>ComponentView&lt;T&gt;</c> — the set
    /// <c>ARCHITECTURE.md</c> Appendix A enumerates on the BCF3014 row.
    /// </summary>
    /// <remarks>
    /// One predicate for the one set, asked from the two opposite directions the design cares about: the
    /// generator asks it of a value written where a runtime value belongs (BCF3014), and
    /// <c>InertSurfaceAnalyzer</c> asks it of the expression itself and of the declaration enclosing it
    /// (BCF3029). Both need every arm, and a second copy would be a second place for a type added to the
    /// surface to be forgotten.
    /// <para>
    /// <c>ComponentView&lt;T&gt;</c> is compared through <see cref="ITypeSymbol.OriginalDefinition"/>
    /// because the resolved symbol is the unbound generic while every written occurrence is a construction
    /// of it. The other three are unconstructed types and compare directly.
    /// </para>
    /// </remarks>
    public bool IsInertDesignTimeType(ITypeSymbol? type) =>
        Matches(type, ViewType)
        // A childless element is an ElementView rather than a View, so without this arm
        // .Param(c => c.Payload, Div) passes through and emits `Div` verbatim.
        || Matches(type, ElementViewType)
        // A content-taking part's call is a SlotView before its brackets, so .Param(c => c.Payload, Card("t"))
        // type-checks through object and would otherwise emit `Card("t")` verbatim, exactly as the
        // ElementView arm above exists to prevent for `Div`.
        || Matches(type, SlotViewType)
        || (type is INamedTypeSymbol named && Matches(named.OriginalDefinition, ComponentViewType));

    /// <summary>
    /// Symbol identity with both null guards in one place: a resolved reference this compilation does not
    /// have, and the <c>SymbolEqualityComparer.Default.Equals(x, null)</c> trap
    /// <see cref="ElementViewType"/>'s remarks describe, which answers <see langword="true"/> for a null
    /// <paramref name="known"/> and would classify an unrelated symbol as part of the surface.
    /// </summary>
    /// <remarks>
    /// Stated once because every predicate below is the same shape, and the guard is the one thing about
    /// them that is easy to leave out and impossible to notice: the failure is a false positive against a
    /// runtime that merely lacks the member, which no test using a current runtime can reach.
    /// </remarks>
    private static bool Matches(ISymbol? symbol, ISymbol? known) =>
        symbol is not null && known is not null && SymbolEqualityComparer.Default.Equals(symbol, known);

    /// <summary>
    /// Whether <paramref name="method"/> carries <c>[ViewPart]</c>, so its body is a design-time expression
    /// the generator expands at each call site rather than a method that runs.
    /// </summary>
    /// <remarks>
    /// Read off <see cref="ISymbol.OriginalDefinition"/>, which is where an attribute is declared: a part
    /// declared inside a generic type reaches a call site as a substituted symbol, and asking that symbol
    /// directly would depend on Roslyn carrying the attribute across the substitution. BCF1002 rejects such
    /// a declaration, so the two answers agree on everything that compiles, and this one does not rest on
    /// the substitution behaviour to do it.
    /// </remarks>
    public bool IsViewPart(IMethodSymbol method)
    {
        if (ViewPartAttributeType is not { } attributeType)
            return false;

        foreach (var attribute in method.OriginalDefinition.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> is a member of the design-time API: a member of
    /// <c>BlazorCodeFirst.Html</c> or <c>BlazorCodeFirst.Decorations</c>, one of the inert types' own
    /// members, or a <c>[ViewPart]</c> method. The set <c>ARCHITECTURE.md</c> §2.1 and §5 name as 設計時API,
    /// the one the IL trimmer removes whole because nothing in it is reachable at run time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Answered from the tables this class already resolves out of the referenced runtime rather than from a
    /// containing-type test, so it is exactly the set the rest of the compiler recognizes: a member the
    /// runtime declares under a name no table has a row for is not translated anywhere either, and a
    /// user-defined <c>Some.Other.Html</c> contributes nothing.
    /// </para>
    /// <para>
    /// The complement matters as much as the set. A user's own declaration that merely returns an inert type
    /// is <em>not</em> a member of this API, and <c>InertSurfaceAnalyzer</c> depends on that: a
    /// <c>View</c>-returning method without <c>[ViewPart]</c> is the Opaque escape hatch
    /// <c>DESIGN.md</c> §5.3 reserves and Appendix B.11(b) refuses to close, so reporting a call to one would
    /// erase a spelling the design deliberately leaves open (#68).
    /// </para>
    /// </remarks>
    /// <remarks>
    /// The property arm splits on <see cref="IPropertySymbol.IsIndexer"/> rather than asking both groups,
    /// because the split is free and total: a curated helper and <c>Slot</c> are named properties, and all
    /// three child lists are indexers, so neither group can answer for a member of the other.
    /// </remarks>
    public bool IsDesignTimeApiMember(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => ClassifySurfaceMethod(method) != SurfaceMethodKind.None || IsViewPart(method),
        IPropertySymbol property => property.IsIndexer
            ? ClassifyChildrenIndexer(property) != ChildrenIndexerKind.None
            : IsElementHelper(property) || IsSlot(property),
        _ => false,
    };

    /// <summary>
    /// Whether a reference of type <paramref name="type"/> to <paramref name="member"/> is a reference to
    /// the design-time surface: an inert-typed expression naming one of the surface's own members.
    /// </summary>
    /// <remarks>
    /// The conjunction, not either half, because either half alone admits the wrong things: a
    /// <c>View</c>-typed local is inert-typed and names nothing on the surface, and a <c>[ViewPart]</c>
    /// used as a value is a surface member reached at a type that is not.
    /// <para>
    /// Two callers ask it from opposite ends. <c>InertSurfaceAnalyzer</c> asks whether an expression the
    /// author wrote is such a reference (BCF3029); <c>RenderExpressionAnalyzer</c> asks whether a callee's
    /// body contains one, which is what makes the <c>View</c> it returns empty at runtime (BCF3030). The
    /// pair lives here for the reason <see cref="IsInertDesignTimeType"/> does: a fourth conjunct, or a
    /// reordering, must be made once.
    /// </para>
    /// <para>
    /// The type test precedes the symbol test because it is the more selective of the two and both are
    /// cheap (#220). The kind prefilter that must precede <em>both</em> belongs to each caller, whose
    /// available prefilter differs: an operation kind on one side, a syntax kind on the other.
    /// </para>
    /// </remarks>
    public bool IsDesignTimeApiReference(ITypeSymbol? type, ISymbol? member) =>
        member is not null
        && IsInertDesignTimeType(type)
        && IsDesignTimeApiMember(member);

    /// <summary>
    /// Whether <paramref name="property"/> is one of the curated element helpers resolved out of the
    /// referenced runtime, keyed the way <see cref="ElementTags"/> is keyed. Checks
    /// <see cref="SvgElementTags"/> too, so a <c>Svg</c> helper is recognized on the same terms as
    /// an <c>Html</c> one (#319 / #534).
    /// </summary>
    /// <remarks>
    /// The normalization is a rule rather than an obvious fact, and a caller that forgets it compiles and
    /// silently answers <see langword="false"/> for every helper. Stated here for the same reason
    /// <see cref="IsSlot"/> is.
    /// </remarks>
    public bool IsElementHelper(IPropertySymbol property) =>
        ElementTags.ContainsKey(Normalize(property)) || SvgElementTags.ContainsKey(Normalize(property));

    /// <summary>
    /// Which of the three <c>params ReadOnlySpan&lt;View&gt;</c> child lists <paramref name="property"/> is,
    /// or <see cref="ChildrenIndexerKind.None"/>.
    /// </summary>
    /// <remarks>
    /// The one place the three indexer identities are compared, and it answers <em>which</em> rather than
    /// merely whether, because its callers want both: <c>RenderExpressionAnalyzer.ClassifyIndexer</c>
    /// dispatches on the kind, <see cref="IsDesignTimeApiMember"/> asks only that it is one, and
    /// <c>UnresolvedValueTypeScanner</c> recognizes two of the three. Written as three separate
    /// comparisons before this, those three sites had already drifted: the scanner's copy was never given
    /// the <c>SlotView</c> arm when #176 added that indexer, and nothing recorded whether the omission was
    /// meant. Expressed as a kind, the scanner's narrower answer is a pattern a reader can see rather than
    /// an arm they have to notice is missing.
    /// </remarks>
    public ChildrenIndexerKind ClassifyChildrenIndexer(IPropertySymbol property)
    {
        var definition = Normalize(property);

        if (Matches(definition, ElementIndexer)) return ChildrenIndexerKind.Element;
        if (Matches(definition, ComponentIndexer)) return ChildrenIndexerKind.Component;
        if (Matches(definition, ContentIndexer)) return ChildrenIndexerKind.Content;

        return ChildrenIndexerKind.None;
    }

    /// <summary>
    /// The parameter roles of a resolved <c>Bind</c> overload, element or component, or
    /// <see langword="false"/> when <paramref name="method"/> is not a shape this compiler was written
    /// against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static, like <see cref="IsVoidTag"/> and <see cref="Normalize(IMethodSymbol)"/>: the answer is a
    /// property of the resolved symbol. Callers must have classified the method as
    /// <see cref="SurfaceMethodKind.Bind"/> or <see cref="SurfaceMethodKind.ComponentBind"/> first —
    /// this answers from shape and does not re-ask.
    /// </para>
    /// <para>
    /// #307 gave it one thing that would otherwise be looked up: a parameter is a role only if its type
    /// is <c>CultureInfo</c>. That is asked by name, in <see cref="IsCultureInfo"/>, rather than against
    /// a symbol this type resolved from the compilation — which is the one place in this file that names
    /// a well-known type instead of resolving it, and so is worth saying rather than leaving a reader to
    /// find. Resolving it would make this an instance member and every caller hold a
    /// <see cref="KnownSymbols"/>, which all five already do, so the cost is not the reason. The reason
    /// is that the type being matched arrives from this repository's own <c>Decorations.Bind</c>
    /// signature: nothing an author writes reaches it, and a shadowing declaration would already have
    /// broken the overload it appears in. Where a name match reads a type an author supplied, resolve it.
    /// </para>
    /// <para>
    /// <paramref name="method"/> is read in whatever spelling it arrives in, and is deliberately
    /// <em>not</em> normalized through <see cref="IMethodSymbol.ReducedFrom"/>. Roslyn answers
    /// <c>GetSymbolInfo</c> with the reduced method for a fluent call and the unreduced one for the
    /// static call, while an operation's <c>IArgumentOperation.Parameter</c> is always unreduced; the
    /// positions are normalized instead, by <see cref="ArgumentIndex(IParameterSymbol)"/>, so the receiver
    /// skip is asked rather than restated. Normalizing the method would additionally make
    /// <see cref="BindParameters.ValueType"/> an unsubstituted type parameter, since
    /// <c>ReducedFrom</c> answers the generic definition.
    /// </para>
    /// <para>
    /// The getter is found by shape rather than at an ordinal, so a new <c>(value type, setter shape)</c>
    /// pair does not have to be transcribed here. <c>ReturnsVoid: false</c> is load-bearing: a
    /// zero-argument <c>Action</c> is a delegate whose invoke method has a non-null <c>void</c> return
    /// type, so without it a callback parameter written ahead of the getter would be read as the getter.
    /// </para>
    /// <para>
    /// Everything written after the getter is a role read off its own type, not off its position. The
    /// three the surface declares are disjoint types — a one-argument delegate over the bound value, a
    /// <see langword="string"/> format, a <c>CultureInfo</c> — so each parameter is asked what it is, and
    /// an overload that reorders them needs no change here. Before #307 the rule was that the parameter
    /// after the getter is the setter or the shape is unreadable, which every culture-taking overload
    /// would have failed. Anything outside the three still leaves the shape unread, which is the same
    /// answer as before: a <c>Bind</c> this compiler was not written against is BCF1003 at the call site,
    /// never a guess at roles it has just admitted it cannot establish.
    /// </para>
    /// </remarks>
    public static bool TryGetBindParameters(IMethodSymbol method, out BindParameters bind)
    {
        bind = default;

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            var getter = method.Parameters[index];

            // A negative index is an extension method's receiver, which argument space excludes. Asking
            // the same rule that places the roles below, rather than spelling the receiver test a second
            // time here, is what keeps the two from ever disagreeing about which parameter is skipped.
            var getterIndex = ArgumentIndex(getter);
            if (getterIndex < 0)
                continue;

            if (getter.Type
                is not INamedTypeSymbol
                {
                    DelegateInvokeMethod: { Parameters.Length: 0, ReturnsVoid: false } getterInvoke,
                })
            {
                continue;
            }

            var valueType = getterInvoke.ReturnType;
            var setterIndex = -1;
            var formatIndex = -1;
            var cultureIndex = -1;
            var setterIsAsynchronous = false;

            for (var after = index + 1; after < method.Parameters.Length; after++)
            {
                var parameter = method.Parameters[after];
                var argumentIndex = ArgumentIndex(parameter);

                if (parameter.Type
                    is INamedTypeSymbol { DelegateInvokeMethod: { Parameters.Length: 1 } setterInvoke })
                {
                    if (setterIndex >= 0
                        || !SymbolEqualityComparer.Default.Equals(setterInvoke.Parameters[0].Type, valueType))
                    {
                        return false;
                    }

                    setterIndex = argumentIndex;
                    setterIsAsynchronous = !setterInvoke.ReturnsVoid;
                    continue;
                }

                if (parameter.Type.SpecialType == SpecialType.System_String)
                {
                    if (formatIndex >= 0)
                        return false;

                    formatIndex = argumentIndex;
                    continue;
                }

                if (IsCultureInfo(parameter.Type))
                {
                    if (cultureIndex >= 0)
                        return false;

                    cultureIndex = argumentIndex;
                    continue;
                }

                return false;
            }

            // A format with no culture is not a shape the surface declares, and reading one would leave
            // the emitter holding a format with no overload of FormatValue to hand it to.
            if (formatIndex >= 0 && cultureIndex < 0)
                return false;

            bind = new BindParameters(
                getterIndex, setterIndex, formatIndex, cultureIndex, valueType, setterIsAsynchronous);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a <c>.Bind</c> format may be written for <paramref name="valueType"/>.
    /// </summary>
    /// <remarks>
    /// Answers <see langword="true"/> when the table is empty, which is the check skipping itself on a
    /// compilation that cannot see the framework's binder extensions. BCF3028 declines the same way for
    /// the same reason: a check with no table to consult reports nothing rather than reporting everything.
    /// </remarks>
    public bool AcceptsBindFormat(ITypeSymbol valueType)
    {
        var types = _formatBindableTypes.Value;
        return types.Count == 0 || types.Contains(valueType);
    }

    /// <summary>
    /// The bound value types of every format-taking <c>CreateBinder</c> overload: the setter parameter's
    /// first type argument, which carries the bound type in both the <c>Action&lt;T&gt;</c> and the
    /// <c>Func&lt;T, Task&gt;</c> shape. <c>BindFormatTableSyncTests</c> holds this set against
    /// <c>BindConverter.FormatValue</c>'s, which the emitter writes the other half of the round trip
    /// against.
    /// </summary>
    private static HashSet<ITypeSymbol> CollectFormatBindableTypes(INamedTypeSymbol binderExtensions)
    {
        var types = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var member in binderExtensions.GetMembers("CreateBinder"))
        {
            // The two constant-time questions first, so the parameter scan is paid only by an overload
            // that could contribute. Parameter 2 is the setter: the list is
            // (factory, receiver, setter, existingValue, …), read unreduced because these are metadata
            // members rather than a resolved call.
            if (member is not IMethodSymbol method
                || method.Parameters.Length < 3
                || method.Parameters[2].Type is not INamedTypeSymbol { TypeArguments.Length: > 0 } setterType
                || !DeclaresFormatParameter(method))
            {
                continue;
            }

            types.Add(setterType.TypeArguments[0]);
        }

        return types;
    }

    /// <summary>
    /// Whether <paramref name="method"/> takes a format, which is what separates the overloads a
    /// <c>.Bind</c> format may be written against from the rest of the <c>CreateBinder</c> group.
    /// </summary>
    private static bool DeclaresFormatParameter(IMethodSymbol method)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Name == "format" && parameter.Type.SpecialType == SpecialType.System_String)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is <c>System.Globalization.CultureInfo</c>, asked by name rather
    /// than against a resolved symbol.
    /// </summary>
    /// <remarks>
    /// <see cref="TryGetBindParameters"/> is static and answers from shape alone, holding no compilation
    /// to resolve a well-known type against. The name is unambiguous at three segments, and a
    /// user-declared <c>System.Globalization.CultureInfo</c> shadowing the framework's would already have
    /// broken the overload it appears in.
    /// </remarks>
    private static bool IsCultureInfo(ITypeSymbol type) =>
        type is INamedTypeSymbol
        {
            Name: "CultureInfo",
            ContainingNamespace:
            {
                Name: "Globalization",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
            },
        };

    /// <summary>
    /// The argument roles of a resolved event decoration, a named shortcut or <c>.On</c>, or
    /// <see langword="false"/> when <paramref name="method"/> is not a shape this compiler was written
    /// against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static, and answered from shape without re-asking the classification, exactly like
    /// <see cref="TryGetBindParameters"/>: callers must have classified the method as
    /// <see cref="SurfaceMethodKind.EventShortcut"/> or <see cref="SurfaceMethodKind.On"/> first. The
    /// method is read in whatever spelling it arrives in and the positions are normalized instead, by
    /// <see cref="ArgumentIndex(IParameterSymbol)"/>, for the reason
    /// <see cref="TryGetBindParameters"/>'s remarks give at length.
    /// </para>
    /// <para>
    /// The handler is required to be both in shape and in position: the one delegate-typed parameter, and
    /// the last argument. The only thing allowed ahead of it is the event's name, a single
    /// <see langword="string"/> — which is the whole of what separates <c>.On("onclick", h)</c> from
    /// <c>.OnClick(h)</c>, read here off the declaration rather than off the classification that
    /// distinguishes the two.
    /// </para>
    /// <para>
    /// A second delegate parameter answers <see langword="false"/> rather than being ranked against the
    /// first. Nothing here could pick between a handler and an options or completion callback, and
    /// inventing a rule for it is how the three conventions this replaces would diverge again. The cost of
    /// answering <see langword="false"/> is a spurious BCF3001 on such an overload's real handler, which
    /// is the safe way round only because it cannot reach an author: <c>KnownSymbolsSyncTests</c> asks
    /// this of every event decoration the runtime declares, so an overload outside this shape is red in
    /// the compiler's own suite before it can ship, with the decision to be made named in the failure.
    /// </para>
    /// </remarks>
    public static bool TryGetEventParameters(IMethodSymbol method, out EventParameters eventParameters)
    {
        eventParameters = default;
        var eventNameIndex = -1;

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            var parameter = method.Parameters[index];

            // A negative index is an extension method's receiver, which argument space excludes.
            var argumentIndex = ArgumentIndex(parameter);
            if (argumentIndex < 0)
                continue;

            if (parameter.Type.TypeKind == TypeKind.Delegate)
            {
                // Anything written after the handler leaves this a shape with no answer here, a second
                // delegate included: that is the rejection the paragraph above is about.
                if (index + 1 != method.Parameters.Length)
                    return false;

                eventParameters = new EventParameters(argumentIndex, eventNameIndex);
                return true;
            }

            if (eventNameIndex >= 0 || parameter.Type.SpecialType != SpecialType.System_String)
                return false;

            eventNameIndex = argumentIndex;
        }

        return false;
    }

    /// <summary>
    /// The argument type the event named <paramref name="eventName"/> delivers, according to the
    /// <c>[EventHandler]</c> metadata this compilation can see, or <see langword="false"/> when the event has
    /// no registration and therefore no mapping to check a handler against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework's own table is consulted first and a registration in the compilation being built cannot
    /// displace one of its names. That order is what makes the walk over the compilation's types rare: a
    /// standard event is answered from metadata, and only a custom name reaches the second table. It is also
    /// the right answer where both register a name, since Blazor's dispatch uses the framework's type.
    /// </para>
    /// <para>
    /// A name registered twice within the compilation, with two different types, is left out of the map
    /// entirely rather than resolved by an order nobody wrote down. Silence under ambiguity is the
    /// under-reporting direction, which is the safe one for a diagnostic that names a specific mistake.
    /// </para>
    /// </remarks>
    public bool TryGetEventArgumentType(
        string eventName, [MaybeNullWhen(false)] out INamedTypeSymbol argumentType)
    {
        argumentType = RegistrationFor(eventName)?.ArgumentType;
        return argumentType is not null;
    }

    /// <summary>
    /// Whether the event named <paramref name="eventName"/> has a registration that disables the modifier
    /// <paramref name="kind"/> asks about, which is the condition BCF3038 reports.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> where no registration answers, on the same terms BCF3028 states for its own
    /// arm: this surface's event names are strings, and a name neither table carries has nothing to
    /// disagree with. A compilation without <c>Components.Web</c> gets that answer for every name the
    /// framework registers, so the check is skipped in silence there rather than reporting everything; a
    /// name the compilation registers itself is still answered. Which flag each modifier reads is decided
    /// where the attribute is read, in <see cref="Collect"/>.
    /// </remarks>
    public bool RefusesEventModifier(string eventName, SurfaceMethodKind kind) =>
        RegistrationFor(eventName) is { } registration
        && !(kind == SurfaceMethodKind.PreventDefault
            ? registration.EnablesPreventDefault
            : registration.EnablesStopPropagation);

    /// <summary>
    /// The registration for <paramref name="eventName"/>, or <see langword="null"/> where neither table
    /// carries the name. Nullable rather than <c>out</c> so that no caller can read a registration whose
    /// <see cref="EventRegistration.ArgumentType"/> was never assigned.
    /// </summary>
    private EventRegistration? RegistrationFor(string eventName) =>
        _frameworkEventRegistrations.Value.TryGetValue(eventName, out var registration)
        || _sourceEventRegistrations.Value.TryGetValue(eventName, out registration)
            ? registration
            : null;

    /// <summary>
    /// One <c>[EventHandler]</c> registration: the argument type it delivers and the two modifier flags.
    /// </summary>
    /// <remarks>
    /// A <see langword="struct"/> and not a class because <c>KnownSymbols</c> is constructed once per
    /// component and per <c>[ViewPart]</c>, and the framework table it builds carries 96 entries; a
    /// reference type would add that many heap allocations to every one of those runs. Not a
    /// <see langword="record"/> struct: its synthesized <c>==</c> would compare
    /// <see cref="ArgumentType"/> by reference rather than through <c>SymbolEqualityComparer</c>, and the
    /// one place two registrations are compared (<see cref="Collect"/>) must not reach for it by accident.
    /// </remarks>
    private readonly struct EventRegistration(
        INamedTypeSymbol argumentType,
        bool enablesStopPropagation,
        bool enablesPreventDefault)
    {
        public INamedTypeSymbol ArgumentType { get; } = argumentType;

        public bool EnablesStopPropagation { get; } = enablesStopPropagation;

        public bool EnablesPreventDefault { get; } = enablesPreventDefault;
    }

    /// <summary>
    /// The <c>[EventHandler]</c> registrations on <paramref name="declaringType"/>, which is how the
    /// framework's table is declared: one attribute per event on a single otherwise empty class.
    /// </summary>
    private static Dictionary<string, EventRegistration> CollectEventRegistrations(
        INamedTypeSymbol declaringType, INamedTypeSymbol eventHandlerAttributeType)
    {
        var registrations = new Dictionary<string, EventRegistration>(System.StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(System.StringComparer.Ordinal);
        Collect(declaringType, eventHandlerAttributeType, registrations, ambiguous);
        return registrations;
    }

    /// <summary>
    /// The <c>[EventHandler]</c> registrations anywhere under <paramref name="root"/>, nested types
    /// included, which is where a consumer's own custom-event registration lives.
    /// </summary>
    private static Dictionary<string, EventRegistration> CollectEventRegistrations(
        INamespaceSymbol root, INamedTypeSymbol eventHandlerAttributeType)
    {
        var registrations = new Dictionary<string, EventRegistration>(System.StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(System.StringComparer.Ordinal);
        CollectFromNamespace(root, eventHandlerAttributeType, registrations, ambiguous);
        return registrations;
    }

    private static void CollectFromNamespace(
        INamespaceSymbol container,
        INamedTypeSymbol eventHandlerAttributeType,
        Dictionary<string, EventRegistration> registrations,
        HashSet<string> ambiguous)
    {
        foreach (var member in container.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol nested:
                    CollectFromNamespace(nested, eventHandlerAttributeType, registrations, ambiguous);
                    break;
                case INamedTypeSymbol type:
                    Collect(type, eventHandlerAttributeType, registrations, ambiguous);
                    break;
            }
        }
    }

    private static void Collect(
        INamedTypeSymbol type,
        INamedTypeSymbol eventHandlerAttributeType,
        Dictionary<string, EventRegistration> registrations,
        HashSet<string> ambiguous)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, eventHandlerAttributeType))
                continue;

            // Read once. ConstructorArguments is virtual and reloads lazy members on a metadata attribute,
            // and this reads four positions out of it rather than the two it used to.
            var arguments = attribute.ConstructorArguments;

            // Both constructors the attribute declares start with (attributeName, eventArgsType); the
            // longer one adds the two modifier flags, in the order (enableStopPropagation,
            // enablePreventDefault) — the reverse of the intuitive one, measured on 10.0.10 (#369). Read by
            // position rather than by parameter name because that is what an attribute's constructor
            // arguments are, and reject anything that does not have the first two.
            if (arguments.Length < 2
                || arguments[0].Value is not string eventName
                || eventName.Length == 0
                || arguments[1].Value is not INamedTypeSymbol argumentType)
            {
                continue;
            }

            if (ambiguous.Contains(eventName))
                continue;

            // The two-argument constructor declares neither flag, and Blazor reads the absent ones as
            // false: a custom event registered with it refuses both modifiers until it is respelled.
            var registration = new EventRegistration(
                argumentType,
                arguments.Length > 2 && arguments[2].Value is true,
                arguments.Length > 3 && arguments[3].Value is true);

            if (registrations.TryGetValue(eventName, out var existing))
            {
                // Compared whole rather than on the argument type alone. Two registrations of one name that
                // agree on the type and disagree on a flag are as ambiguous as two that disagree on the
                // type, and first-wins would answer one of the two by an order nobody wrote down.
                if (SymbolEqualityComparer.Default.Equals(existing.ArgumentType, registration.ArgumentType)
                    && existing.EnablesStopPropagation == registration.EnablesStopPropagation
                    && existing.EnablesPreventDefault == registration.EnablesPreventDefault)
                {
                    continue;
                }

                ambiguous.Add(eventName);
                registrations.Remove(eventName);
                continue;
            }

            registrations[eventName] = registration;
        }

        foreach (var nested in type.GetTypeMembers())
            Collect(nested, eventHandlerAttributeType, registrations, ambiguous);
    }

    private static SurfaceMethodKind ClassifyComponentParameterDefinition(
        IMethodSymbol method,
        INamedTypeSymbol componentViewType,
        INamedTypeSymbol? viewType,
        INamedTypeSymbol? renderFragmentType,
        INamedTypeSymbol? renderFragmentGenericType,
        INamedTypeSymbol? funcWithArgumentType,
        INamedTypeSymbol? eventCallbackType,
        INamedTypeSymbol? eventCallbackNonGenericType,
        INamedTypeSymbol? actionType,
        INamedTypeSymbol? actionGenericType,
        INamedTypeSymbol? funcType)
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

            // The non-generic EventCallback overload (#492): .Param(c => c.OnClose, Action) or
            // .Param(c => c.OnClose, Func<Task>). Arity 0, like FragmentParam above, so the receiver's
            // selected type is what tells them apart.
            if (method.Arity == 0
                && eventCallbackNonGenericType is not null
                && SymbolEqualityComparer.Default.Equals(selectedType, eventCallbackNonGenericType)
                && IsParameterlessHandler(method.Parameters[1].Type, actionType, funcType))
            {
                return SurfaceMethodKind.ComponentParamEventCallback;
            }

            // The two generic EventCallback<TArg> overloads (#492): .Param(c => c.OnPicked, Action<TArg>) /
            // Func<TArg, Task> for a handler that receives the raised value, and .Param(c => c.OnValidSubmit,
            // Action) / Func<Task> for one that ignores it — the shape EventCallbackFactory.Create<T> itself
            // offers beside Action<T>/Func<T, Task>, and the one a parameterless handler like Razor's own
            // OnValidSubmit="HandleCreate" needs. Both share arity 1 and the same selected-type check —
            // there the selected type IS the method's own type parameter for ScalarParam above, while here
            // it is EventCallback<TArg> wrapping it, so ScalarParam's check never matches either of these —
            // and differ only in whether the handler's own type parameter is TArg or absent.
            if (method.Arity == 1
                && eventCallbackType is not null
                && selectedType is INamedTypeSymbol { TypeArguments.Length: 1 } eventCallback
                && SymbolEqualityComparer.Default.Equals(eventCallback.OriginalDefinition, eventCallbackType)
                && SymbolEqualityComparer.Default.Equals(eventCallback.TypeArguments[0], method.TypeParameters[0])
                && method.Parameters[1].Type is INamedTypeSymbol genericHandlerType
                && ((actionGenericType is not null
                        && genericHandlerType.TypeArguments.Length == 1
                        && SymbolEqualityComparer.Default.Equals(
                            genericHandlerType.OriginalDefinition, actionGenericType)
                        && SymbolEqualityComparer.Default.Equals(
                            genericHandlerType.TypeArguments[0], method.TypeParameters[0]))
                    || (genericHandlerType.TypeArguments.Length == 2
                        && SymbolEqualityComparer.Default.Equals(
                            genericHandlerType.OriginalDefinition, funcWithArgumentType)
                        && SymbolEqualityComparer.Default.Equals(
                            genericHandlerType.TypeArguments[0], method.TypeParameters[0]))
                    || IsParameterlessHandler(genericHandlerType, actionType, funcType)))
            {
                return SurfaceMethodKind.ComponentParamEventCallback;
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
    /// Whether <paramref name="handlerType"/> is a plain <c>Action</c> or <c>Func&lt;Task&gt;</c> — the
    /// shape neither of the two EventCallback-aware <c>.Param</c> overload pairs' argument-ignoring member
    /// wraps a raised value in (#492). Shared by the non-generic <c>EventCallback</c> overloads, whose
    /// handler never carries a value, and the generic <c>EventCallback&lt;TArg&gt;</c> pair that ignores
    /// the one it is offered.
    /// </summary>
    private static bool IsParameterlessHandler(
        ITypeSymbol handlerType, INamedTypeSymbol? actionType, INamedTypeSymbol? funcType) =>
        (actionType is not null && SymbolEqualityComparer.Default.Equals(handlerType, actionType))
        || (funcType is not null
            && SymbolEqualityComparer.Default.Equals(handlerType.OriginalDefinition, funcType));

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
    /// channel. That is the same reason the decoration capture below filters on the <c>ElementView</c>
    /// receiver rather than on the member's name.
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
    /// How many distinct <see cref="Compilation"/> instances <see cref="TryCreate"/> keeps a build for at
    /// once, evicting the oldest once a new compilation would exceed it (#514). A <see cref="Compilation"/>
    /// key is never explicitly removed on its own, since <c>KnownSymbols</c> holds symbols resolved from
    /// it and so holds a reference back to it; without a cap that would pin every compilation a long-running
    /// IDE session has ever produced. Sized to the same figure Roslyn's own equivalent
    /// (<c>WellKnownTypeProvider</c>'s <c>BoundedCacheWithFactory</c>) uses for the same reason.
    /// </summary>
    private const int CacheCapacity = 5;

    /// <summary>Exposes <see cref="CacheCapacity"/> to <c>KnownSymbolsCacheTests</c> without making the
    /// figure itself part of the public contract callers reason about.</summary>
    internal const int CacheCapacityForTests = CacheCapacity;

    private static readonly object CacheLock = new();
    private static readonly Queue<Compilation> CacheOrder = new();
    private static readonly Dictionary<Compilation, KnownSymbols> Cache = [];

    /// <summary>
    /// Resolves <c>BlazorCodeFirst.Html</c> from the given compilation and returns a populated instance,
    /// or <see langword="null"/> when the type cannot be found (e.g., the runtime assembly is not referenced).
    /// </summary>
    /// <remarks>
    /// Memoized per <paramref name="compilation"/> instance (#514): building one costs around 20
    /// <c>GetTypeByMetadataName</c> calls plus four full <c>GetMembers()</c> walks, and this is called once
    /// per candidate component and once per <c>[ViewPart]</c> method, all against the same compilation. The
    /// null case is not cached — <c>ComponentModelFactory.Analyze</c> and the <c>[ViewPart]</c> transform
    /// both call this only after already filtering to nodes their own cheaper checks expect to resolve, so a
    /// null result is a defensive path that is not expected to run often enough to be worth a cache entry.
    /// </remarks>
    public static KnownSymbols? TryCreate(Compilation compilation)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(compilation, out var cached))
                return cached;

            var htmlType = compilation.GetTypeByMetadataName("BlazorCodeFirst.Html");
            if (htmlType is null)
                return null;

            var created = new KnownSymbols(htmlType, compilation);

            if (CacheOrder.Count >= CacheCapacity)
                Cache.Remove(CacheOrder.Dequeue());

            CacheOrder.Enqueue(compilation);
            Cache[compilation] = created;
            return created;
        }
    }

}

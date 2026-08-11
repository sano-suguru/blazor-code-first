using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Diagnostics;

internal static class DiagnosticDescriptors
{
    /// <summary>
    /// BCF1001: A class that declares the design-time expression override (<c>Body</c> on
    /// <c>BodyComponentBase</c>, <c>Chrome</c> on <c>ChromeLayoutBase</c>) must be declared
    /// <c>partial</c> so the source generator can emit the <c>RenderView</c> override into the same class.
    /// A class that only inherits a BlazorCodeFirst base without declaring the override has nothing generated
    /// into it and is not reported.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF1001 = new(
        id: "BCF1001",
        title: "Class declaring a design-time expression must be partial",
        messageFormat: "'{0}' declares the {1} design-time expression of {2} but is not declared partial; add the partial modifier",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A class that declares the design-time expression override (Body on BodyComponentBase, " +
            "Chrome on ChromeLayoutBase) must be declared partial so the source generator can emit the " +
            "RenderView override into the same class. A class that only inherits a BlazorCodeFirst base without " +
            "declaring the override (an intermediate abstract base, a leaf whose base already declares " +
            "it, or a re-abstraction) has nothing generated into it and needs no partial modifier.");

    /// <summary>
    /// BCF1002: A <c>[ViewPart]</c> method does not satisfy the source generator's supported
    /// static-expansion contract.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF1002 = new(
        id: "BCF1002",
        title: "ViewPart method shape is unsupported",
        messageFormat: "ViewPart method '{0}' is unsupported: {1}",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A method marked [ViewPart] must satisfy the compiler's supported static expansion contract.");

    /// <summary>
    /// BCF3001: A BlazorCodeFirst base's design-time expression getter (<c>Body</c> on <c>BodyComponentBase</c>,
    /// <c>Chrome</c> on <c>ChromeLayoutBase</c>) must not mutate component state (single-direction
    /// data-flow violation).
    /// The initial detectable boundary covers statically identifiable direct writes (field assignments,
    /// property assignments, and increment/decrement operators) whose target is an instance member of
    /// the containing component. Recognized deferred event handler lambdas (the <c>Button</c> onClick
    /// argument) are excluded. Arbitrary interprocedural side effects are not guaranteed to be detected.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3001 = new(
        id: "BCF3001",
        title: "State mutation inside the design-time expression violates single-direction data flow",
        messageFormat: "'{0}' is mutated inside {1}; move state changes to event handlers to preserve single-direction data flow",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The design-time expression getter (Body or Chrome) must be a pure projection of state to UI. " +
            "Mutating component state inside it causes render-time side effects that can corrupt " +
            "the rendering pipeline. Move the mutation to an event handler.");

    /// <summary>
    /// BCF3002: A <c>ForEach</c> key selector does not reference its item and therefore cannot express
    /// per-item identity, defeating keyed diffing. Heuristic and intentionally conservative: it does not
    /// detect an item-derived-but-index-like key.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3002 = new(
        id: "BCF3002",
        title: "ForEach key selector may not identify items",
        messageFormat: "ForEach key selector does not reference the item; a key must identify each item so list state is preserved across reorders",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A ForEach key selector should return a value derived from the item so Blazor can preserve " +
            "per-row state across insertion, removal, and reordering. A key that ignores the item " +
            "(a constant, an external index, or another list's item) forces full re-rendering.");

    /// <summary>
    /// BCF3003: A <c>ForEach</c> content template's root is not a single element or component, so its key
    /// has no frame to attach to (Blazor's <c>SetKey</c> keys the currently open element/component frame).
    /// The required-key contract cannot be honored, so emission is suppressed. Mirrors Razor, where
    /// <c>@key</c> cannot be applied to an <c>@if</c>; wrap the content in an element such as
    /// <c>Html.Div[...]</c> instead.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3003 = new(
        id: "BCF3003",
        title: "ForEach content must be a single element or component",
        messageFormat: "ForEach content must be a single element or component so its key can be applied; wrap it in a container such as Html.Div[...]",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A ForEach key is applied to the content's root element or component frame. When the content " +
            "root is a region (a bare If or nested ForEach, or a view part whose body is region-rooted) " +
            "or bare text (a plain string value with no wrapping element), there is no frame to key, so the " +
            "required key cannot be applied. A Fragment (wrapper-less grouping), Raw (raw markup), and an " +
            "externally supplied RenderFragment placed as content also open no single keyable frame. Wrap " +
            "the content in a container element such as Html.Div[...].");

    /// <summary>
    /// BCF1003: A component's design-time expression (<c>Body</c> or <c>Chrome</c>) reached the model
    /// stage but could not be translated to a RenderView (no template, and no other actionable
    /// diagnostic was produced). Explains the CS0534 that the abstract RenderView would otherwise raise
    /// on its own. Transitional: its firing condition shrinks once the Opaque/Transplantable fallback
    /// paths are implemented.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF1003 = new(
        id: "BCF1003",
        title: "Design-time expression could not be translated",
        messageFormat: "The {1} design-time expression of '{0}' could not be translated to a RenderView; it uses a construct that is not statically analyzable",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The design-time expression could not be classified into the statically sequenceable subset " +
            "and no runtime fallback exists yet, so no RenderView is generated. Use the supported " +
            "element helpers, combinators, Component<T>(), Fragment, Raw, or an inline expression " +
            "lambda, so the expression can be analyzed.");

    /// <summary>
    /// BCF1004: A design-time expression override declares a getter the generator cannot translate,
    /// either a getter body that does not reduce to a single expression, or an auto property, which
    /// declares no getter body at all. Distinct from BCF1003: the getter's shape is the problem, not the
    /// constructs used inside it, and the fix is to rewrite the getter rather than to change which
    /// element helpers are read. Reported at the property identifier rather than inside the getter, which is
    /// the same distinction: BCF1003 blames an expression, BCF1004 blames the declaration around it. Not
    /// reported when the component overrides <c>RenderView</c> by hand (the design-time expression is
    /// then unused and the code is correct), nor for a partial property with no implementation part
    /// (CS9248 already names it).
    /// </summary>
    public static readonly DiagnosticDescriptor BCF1004 = new(
        id: "BCF1004",
        title: "Design-time expression getter must reduce to a single expression",
        messageFormat: "'{0}' declares the {1} design-time expression with a getter that does not reduce to a single expression; write it as '=> expr', 'get => expr', or 'get {{ return expr; }}'",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A design-time expression is an inert projection of state to UI that the generator translates " +
            "statically; it is never evaluated at runtime. Its getter must therefore reduce to a single " +
            "expression. A getter that contains statements, for example a local variable declared before " +
            "the return, would require the Transplantable path, which is not implemented. An auto " +
            "property declares no getter to translate at all. Supply RenderView by hand if the body " +
            "cannot be expressed as a single expression.");

    /// <summary>
    /// BCF1005: A nested class declares a design-time expression. Emitting <c>RenderView</c> into it would
    /// require reproducing the enclosing type chain (including any enclosing type's type parameters),
    /// which is not supported, so nothing is generated. Explains the CS0534 that the abstract RenderView
    /// would otherwise raise on its own, which names only RenderView and never mentions the nesting.
    /// Transitional: its firing condition disappears if nested components become supported.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF1005 = new(
        id: "BCF1005",
        title: "Nested class cannot declare a design-time expression",
        messageFormat: "'{0}' declares the {1} design-time expression but is a nested type; move it to a top-level type",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The generated RenderView is emitted into a top-level partial class. A nested component would " +
            "require the generated code to reproduce every enclosing type declaration, which is not " +
            "supported. Move the component to a top-level type.");

    /// <summary>
    /// BCF3004: A <c>ForEach</c> content or key is not an inline expression lambda (for example a block-bodied
    /// lambda or a method group), so it cannot be statically analyzed. Transitional: narrows once the
    /// Transplantable/Opaque paths support such content.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3004 = new(
        id: "BCF3004",
        title: "ForEach content must be an inline expression lambda",
        messageFormat: "ForEach content and key must be inline expression lambdas so they can be statically analyzed; wrap the call in a lambda such as x => Wrapper(x)",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A ForEach content or key selector must be an inline expression lambda (item => ...). A " +
            "block-bodied lambda or a method group cannot be statically sequenced. Rewrite it as an inline " +
            "expression lambda, wrapping any helper call as x => Helper(x).");

    /// <summary>
    /// BCF3005: A <c>Component&lt;T&gt;()</c> parameter-binding selector is not a simple property selection
    /// on its own lambda parameter (for example <c>c =&gt; c.Label</c>). Casts, method calls,
    /// null-conditional access, or a member of a captured variable cannot be turned into a static parameter
    /// setter. Every channel that names a parameter with a selector answers to this rule:
    /// <c>.Param</c>, <c>.Template</c>, and <c>.Bind</c>.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3005 = new(
        id: "BCF3005",
        title: "Component parameter selector must be a simple property selection",
        messageFormat: "Component parameter selector must select a property of the lambda parameter, such as c => c.Label",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>() names the parameter to bind with a selector of the form c => c.Property, so the " +
            "source generator can emit a static parameter setter. .Param, .Template, and .Bind all take that " +
            "same selector. A cast, method call, null-conditional access, or a member of a captured variable " +
            "cannot be statically resolved to a parameter name.");

    /// <summary>
    /// BCF3006: A <c>Component&lt;T&gt;()</c> parameter binding (<c>.Param</c>, <c>.Template</c>, or
    /// <c>.Bind</c>) targets a property that is not a settable <c>[Parameter]</c>. Setting a non-parameter
    /// (or a parameter with no accessible setter) would throw at runtime, so it is rejected at compile time.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3006 = new(
        id: "BCF3006",
        title: "Component parameter target must be a settable [Parameter] property",
        messageFormat: "'{0}' is not a settable [Parameter] property; only a property marked [Parameter] with an accessible setter can be bound",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>() binds a parameter through .Param, .Template, or .Bind, and each of them can " +
            "only target a property marked [Parameter] with a public setter. Binding any other member " +
            "would throw at runtime when Blazor applies the parameters.");

    /// <summary>
    /// BCF3007: A <c>Component&lt;T&gt;()</c> chain binds the same property more than once, counting every
    /// channel that can bind one: <c>.Param</c>, <c>.Template</c>, <c>.Bind</c>, and child content written
    /// in brackets. Blazor silently applies the last binding, so a duplicate is almost certainly a mistake;
    /// it is rejected at compile time rather than allowed to shadow a value at runtime.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3007 = new(
        id: "BCF3007",
        title: "Component parameter is bound more than once",
        messageFormat: "'{0}' is bound more than once; remove the duplicate binding",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>() must bind each parameter at most once per chain, counting every channel that " +
            "can bind one: .Param, .Template, .Bind, and child content written in brackets. Binding the " +
            "same property twice makes the earlier value dead, because Blazor applies the last write, so " +
            "the duplicate is reported at compile time.");

    /// <summary>
    /// BCF3008: A decoration (<c>.Class</c>/<c>.Attr</c>/a named attribute shortcut/<c>.OnClick</c>/<c>.On</c>)
    /// was applied to a node that does not open a single HTML element (an <c>If</c>/<c>ForEach</c> region
    /// root, a <c>Fragment</c>/<c>Raw</c> wrapper-less node, or a <c>[ViewPart]</c>/Component call result).
    /// Decorations fold into the attributes of the element opened by an Html element helper or
    /// <c>Html.Element</c>, so there must be such an element to attach to.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3008 = new(
        id: "BCF3008",
        title: "Decoration target must be a single element",
        messageFormat: "A decoration can only be applied to a single element (an Html element such as Div/Span/Button, or Html.Element); it cannot be applied to If, ForEach, Fragment, Raw, or a [ViewPart]/Component result",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A decoration folds into the owning element's attributes, so it can only be applied to a node " +
            "that opens a single HTML element (an Html element helper or Html.Element). Applying it to a " +
            "region-rooted or wrapper-less node (If, ForEach, Fragment, Raw) or a " +
            "[ViewPart]/Component result has no element to attach to. Decorate a concrete element instead.");

    /// <summary>
    /// BCF3009: <c>Html.Element</c> was called with a tag argument that is not a non-empty compile-time
    /// constant string. A non-empty constant tag keeps the element declarative and predictable
    /// (design-time syntax the generator can lower to a literal <c>OpenElement</c>); this is not an
    /// AOT/sequencing constraint, and non-constant or empty tags are not a security (injection) concern.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3009 = new(
        id: "BCF3009",
        title: "Element tag must be a compile-time constant string",
        messageFormat: "Html.Element tag must be a non-empty compile-time constant string; use a non-empty " +
            "string literal or a const",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Html.Element(tag, ...) lowers the tag to a literal OpenElement call, so the tag must be a " +
            "non-empty compile-time constant string. This keeps the vocabulary declarative and " +
            "predictable, consistent with the design-time nature of the surface.");

    /// <summary>
    /// BCF3010: An attribute or event is bound more than once on the same element. Neither outcome is what
    /// the author wrote: two bindings in the attribute channel leave the earlier one dead (the last write
    /// wins), while one name bound through the attribute channel and the event channel keeps both, so an
    /// inline handler and a C# handler each fire on every event. <c>class</c> is the sole exception,
    /// multiple <c>.Class</c>/<c>.Attr("class", …)</c> fold into one space-joined attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3010 = new(
        id: "BCF3010",
        title: "Attribute or event is bound more than once",
        messageFormat: "'{0}' is bound more than once on this element; remove the duplicate (only 'class' may be repeated, because it folds)",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every attribute and event other than 'class' is single-binding. Two bindings of one name in " +
            "the attribute channel make the earlier value dead, because the last write wins; a name bound " +
            "once through the attribute channel and once through the event channel keeps both, so an " +
            "inline handler and a C# handler both fire. Neither is what the author asked for, so the " +
            "duplicate is reported at compile time. Multiple .Class or .Attr(\"class\", …) decorations " +
            "fold into a single class attribute.");

    /// <summary>
    /// BCF3011: A <c>.Attr</c> name or <c>.On</c> event name is not a non-empty compile-time constant
    /// string. A constant name keeps the decoration declarative and typo-checkable, and is required to
    /// route class-folding and detect duplicate bindings. Sibling of BCF3009 (constant Element tag).
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3011 = new(
        id: "BCF3011",
        title: "Attribute/event name must be a compile-time constant string",
        messageFormat: ".Attr name and .On event name must be a non-empty compile-time constant string",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            ".Attr(name, …) and .On(eventName, …) lower the name to a literal attribute, so it must be a " +
            "non-empty compile-time constant string. This keeps the vocabulary declarative and " +
            "typo-checkable and is the prerequisite for class folding and duplicate-binding detection.");

    /// <summary>
    /// BCF3012: The type argument of <c>Component&lt;T&gt;()</c> did not resolve to a type while the
    /// generator ran. The dominant cause is a <c>.razor</c> component declared in the same project: the
    /// Razor compiler is itself a source generator, and source generators cannot observe each other's
    /// output, so the type is unresolved here even though it exists in the final compilation. The same
    /// component in a referenced project or NuGet package resolves normally, as does a hand-authored C#
    /// component.
    /// </summary>
    /// <remarks>
    /// An error rather than a pass-through because both alternatives are worse, and both were measured.
    /// An unresolved type argument is emitted as the written name with no qualification, and the
    /// generated file carries no <c>using</c> directives, so the emitted <c>OpenComponent&lt;T&gt;</c>
    /// either fails with a CS0246 inside generated code, which the author cannot fix from their own
    /// file, or binds silently to a different same-named type that happens to be reachable from the
    /// generated file's namespace, rendering the wrong component with no diagnostic at all.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3012 = new(
        id: "BCF3012",
        title: "Component type argument could not be resolved",
        messageFormat: "'{0}' could not be resolved when the BlazorCodeFirst generator ran; a .razor " +
            "component declared in this project is invisible to it because source generators cannot " +
            "observe each other's output. Move it to a referenced project, write it as a hand-authored " +
            // The trailing period is required: RS1032 rejects a multi-sentence message without one.
            "C# component, or fix the name.",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>() lowers its type argument to a literal OpenComponent<T> call, so the type must " +
            "resolve while the generator runs. A .razor component declared in the same project does not: " +
            "the Razor compiler is a source generator too, and source generators cannot observe each " +
            "other's output. The same component in a referenced project or NuGet package resolves " +
            "normally, as does one written by hand in C#. When the cause is instead a typo, an inaccessible " +
            "type, an ambiguous name, or a missing using directive, a C# resolution error is also reported " +
            "at the same position.");

    /// <summary>
    /// BCF3013: <c>Component&lt;T&gt;()[children]</c> was given child content but <c>T</c> has no parameter
    /// that can receive it, no <c>ChildContent</c> at all, one that is not a settable
    /// <c>[Parameter]</c>, or one typed <c>RenderFragment&lt;TContext&gt;</c> rather than the non-generic
    /// <c>RenderFragment</c>. Blazor would throw while applying parameters.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3013 = new(
        id: "BCF3013",
        title: "Component cannot receive child content",
        messageFormat:
            "'{0}' has no settable [Parameter] named 'ChildContent' of type RenderFragment, so it cannot "
                + "receive child content; bind a fragment parameter with .Param(c => c.Name, content) instead",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>()[children] binds child content to a parameter named ChildContent, mirroring how "
                + "Razor lowers nested content. The parameter must be a settable [Parameter] whose type is "
                + "the non-generic RenderFragment; a RenderFragment<TContext> cannot receive it. Without "
                + "such a parameter Blazor throws while applying parameters, so it is rejected at compile "
                + "time.");

    /// <summary>
    /// BCF3014: an inert design-time value (<c>View</c>, <c>ElementView</c>, <c>ComponentView&lt;T&gt;</c>,
    /// or <c>SlotView</c>) was passed to the generic <c>Param</c>, whose value is emitted
    /// verbatim. Such a value binds the empty design-time marker rather than any content: an
    /// <c>object</c>-typed parameter accepts it with no exception at all and renders wrong output, and a
    /// typed parameter throws an invalid cast at runtime.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3014 = new(
        id: "BCF3014",
        title: "Design-time value bound as a component parameter value",
        messageFormat:
            "'{0}' is inert design-time syntax and cannot be bound as a parameter value; use "
                + ".Param(c => c.Name, content) on a RenderFragment parameter to pass content",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "View and ComponentView<T> are inert markers read by the source generator, not runtime "
                + "values. The generic Param emits its value expression verbatim, so binding one of them "
                + "assigns the empty marker: an object-typed parameter silently receives it and renders "
                + "wrong output, while a typed parameter throws an invalid cast when Blazor applies "
                + "parameters. Bind content through the RenderFragment overload of Param instead.");

    /// <summary>
    /// BCF3015: a type reference inside a design-time value expression could not be resolved while the
    /// generator ran and its written form depends on lexical context absent from generated code.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3015 = new(
        id: "BCF3015",
        title: "Type reference cannot be safely emitted",
        messageFormat:
            "'{0}' could not be resolved when the BlazorCodeFirst generator ran and is not qualified with "
                + "global::, so it cannot be safely emitted into generated code. Fully qualify the type, "
                + "move a generated type to a referenced project, write it as hand-authored C#, or fix "
                + "the name.",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Design-time value expressions are transplanted into a generated file with no using directives. "
                + "Resolved type references are fully qualified, but an unresolved context-dependent name "
                + "cannot be normalized safely. A reference rooted at global:: is already context-independent "
                + "and remains subject to ordinary C# resolution.");

    /// <summary>
    /// BCF3016: children were given to one of the HTML standard's void elements, either through a curated
    /// helper (<c>Img["x"]</c>) or through <c>Element</c> with a void tag (<c>Element("img")["x"]</c>).
    /// A void element has no closing tag, so the design-time tree does not survive being serialized and
    /// parsed again: static SSR emits a closing tag the parser does not accept, and the children end up
    /// outside the element, while interactive rendering places them inside it.
    /// </summary>
    /// <remarks>
    /// The first member of the class of breaks decidable from the element tag alone (<c>DESIGN.md</c>
    /// §4.1, measured on 2026-08-03). It is not a validity check: whether a given child is allowed inside
    /// a given parent needs the (parent, child) pair and stays unchecked by design. Unknown tags and
    /// custom elements are silent for the same reason the curated set is defined by a rule, there is no
    /// standard to read them against.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3016 = new(
        id: "BCF3016",
        title: "Void element cannot have children",
        messageFormat:
            "'{0}' is a void element and cannot have children; prerendering pushes them out of the element "
                + "while interactive rendering keeps them inside, so the two disagree. Remove the children, "
                + "or place them beside the element.",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A void element (area, base, br, col, embed, hr, img, input, link, meta, source, track, wbr) "
                + "has no closing tag, so children written on it do not round-trip. Static SSR serializes "
                + "a closing tag, which the HTML parser does not accept: it pushes the children out to "
                + "siblings, and a stray closing tag can be re-read as a start tag, so a Br[\"x\"] becomes "
                + "two <br> elements. Interactive rendering has no parser in the way and places the same "
                + "children inside the element, so prerendering and interactive rendering produce "
                + "different DOM for one expression. Attributes are the way to configure a void element; "
                + "content belongs next to it.");

    /// <summary>
    /// BCF3017: A <c>.Bind</c> getter is not an inline lambda with an expression body. The body is
    /// transplanted twice — as the attribute value and as the binder's current value — so it has to be
    /// available as an expression.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3017 = new(
        id: "BCF3017",
        title: "Bind getter is not an inline expression lambda",
        messageFormat: "The '.Bind' getter must be an inline lambda with an expression body, such as "
            + "'() => _name'. A block-bodied lambda or a method group cannot be read as an expression.",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The generator transplants the getter's body twice: once as the bound attribute's value, and "
                + "once as the binder's current value. A block-bodied lambda and a method group both hide "
                + "that expression behind a call, leaving nothing to transplant. This is the same "
                + "restriction BCF3004 places on ForEach's content and key, for the same reason. The "
                + "setter argument carries no such restriction, because it is handed to EventCallback "
                + "whole and never taken apart.");

    /// <summary>
    /// BCF3018: A getter-only <c>.Bind</c>'s getter body is not assignable, so no setter can be built
    /// from it. Argument counts are not used to name the forms: the same form is three arguments on an
    /// element and two on a component, so a count is wrong on one of the two surfaces this fires on.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3018 = new(
        id: "BCF3018",
        title: "Bind target is not assignable",
        messageFormat: "'{0}' cannot be assigned to, so no setter can be derived from it. Write the "
            + "setter explicitly as the last argument of '.Bind', for example "
            + "'.Bind(\"value\", \"oninput\", () => Query, v => Query = v.Trim())' on an element or "
            + "'.Bind(c => c.Value, () => Query, v => Query = v.Trim())' on a component.",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The getter-only form derives its setter by placing the getter's body on the left of an "
                + "assignment, so that body has to be assignable: a field, a settable property, or an "
                + "element access whose indexer has a setter, alone or at the end of a member chain. A "
                + "call, an operator, a get-only property and a readonly field are none of those. A local "
                + "variable, a parameter and a ForEach iteration variable are rejected even though C# "
                + "would assign to them, because the design-time expression is a property getter: those "
                + "die with each render and the write-back would not survive to the next one. A member of "
                + "an iteration variable is accepted, because it writes through to the element in the "
                + "source list. Anything outside that set needs a setter written at the call site.");

    /// <summary>
    /// BCF3019: An event name does not begin with <c>on</c>. Blazor's event attribute names always do,
    /// and a name that does not is added as a plain attribute whose handler never fires.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3019 = new(
        id: "BCF3019",
        title: "Event name does not begin with 'on'",
        messageFormat: "'{0}' is not an event attribute name. Blazor event names begin with 'on' "
            + "(for example 'oninput', 'onchange'); the prefix is never added for you.",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A name without the prefix reaches AddAttribute as an ordinary attribute rather than an event "
                + "registration, so the handler is never called and nothing reports it at runtime. The "
                + "surface never adds the prefix for the author, which is what makes the omission worth "
                + "diagnosing. On '.Bind' the check does a second job: the attribute name and the event "
                + "name are adjacent string arguments, and swapping them compiles, so this is what stops "
                + "a swapped pair.");

    /// <summary>
    /// BCF3020: The component has no <c>{name}Changed</c> parameter of the bound type, so the binding's
    /// write-back has nowhere to go.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3020 = new(
        id: "BCF3020",
        title: "Component has no matching change callback",
        messageFormat: "'{0}' declares no settable '[Parameter]' named '{1}' of type "
            + "'EventCallback<{2}>', so '{3}' cannot be bound in both directions. Bind it one way with "
            + "'.Param' instead.",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component binding derives its parameter names rather than taking them from the author, which "
                + "is the opposite of the element surface. That is only sound because the derivation can "
                + "be checked: the component's type is known, so the generator looks the derived name up "
                + "and reports this when it is absent or carries the wrong type. Element binding has no "
                + "such check available, which is why it makes the author write both names instead.");

    /// <summary>
    /// BCF3022: The content argument of the contextual <c>Component&lt;T&gt;().Template</c> overload is not
    /// an inline expression lambda, so the generator has no expression to sequence and no parameter symbol
    /// to substitute the generated context variable for. A method group, an anonymous method, and a
    /// block-bodied lambda all hide the content behind a call.
    /// </summary>
    /// <remarks>
    /// Sibling of BCF3004, which places the same restriction on <c>ForEach</c>'s content and key for the
    /// same reason. Arity is not this rule's concern: a zero-parameter or multi-parameter lambda does not
    /// convert to <c>Func&lt;TContext, View&gt;</c> at all, so C# rejects the call before this rule could
    /// apply. The number follows BCF3021, which was withdrawn (付録B.5) and stays retired.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3022 = new(
        id: "BCF3022",
        title: "Generic fragment template must be an inline expression lambda",
        messageFormat: "Generic fragment template must be an inline expression lambda so it can be statically analyzed; write it as context => content",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>().Template contextual content must be an inline expression lambda. " +
            "Method groups, anonymous methods, and block-bodied lambdas cannot be statically sequenced.");

    /// <summary>
    /// BCF3023: <c>.Attr("class", …)</c> carries a value the class channel cannot join as text — a
    /// <see langword="bool"/>, whether the author wrote one or reached the bare <c>.Attr("class")</c>
    /// spelling. That name folds into the channel, which joins its decorations into one value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike its siblings, this rule is not about a value that fails to translate; the value translates
    /// two different ways. With one class decoration on the element the channel emits the value alone, so
    /// <c>AddAttribute(int, string, bool)</c> binds and <see langword="true"/> renders <c>class=""</c>,
    /// emptying the class list. With two or more the channel joins them with <c>+</c>, so the same
    /// <see langword="true"/> is string-concatenated and renders <c>class="a True"</c> (both measured,
    /// #159). One spelling therefore means two things depending on a count written elsewhere in the chain,
    /// which is the translation defect BCF3016's principle names, arrived at from the generator's own fold
    /// rather than from the HTML parser.
    /// </para>
    /// <para>
    /// <c>.Attr("class")</c> reaches the same rule without the author writing a <see langword="bool"/>
    /// anywhere: the bare spelling stands for a presence, and a presence has no text (#178). It is reported
    /// at the decoration's name, there being no value argument to point at.
    /// </para>
    /// <para>
    /// The name is what makes this reachable, not the overload: <c>.Attr("disabled", flag)</c> is exactly
    /// what the <see langword="bool"/> overload exists for (#158), <c>.Attr("disabled")</c> is what the bare
    /// one exists for, and only <c>"class"</c> folds.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3023 = new(
        id: "BCF3023",
        title: "Class attribute value must be a string",
        messageFormat: "'class' folds into the class channel, which joins its values as text, so a bool has no meaning there; write the condition as a string expression such as .Class(condition ? \"name\" : null)",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The bool overload of .Attr is Blazor's conditional-attribute form, and the bare .Attr(name) " +
            "spelling stands for a presence; neither carries over to the class channel, where values are " +
            "concatenated. Use .Class or the string overload of .Attr.");

    /// <summary>
    /// BCF3024: an element carries both a class-channel decoration (<c>.Class</c> or
    /// <c>.Attr("class", …)</c>) and a <c>.Bind</c> whose attribute name is <c>class</c>. The channel folds
    /// its decorations into one frame; the binding emits its own from the bindings loop and joins nothing.
    /// The element is therefore emitted with the attribute twice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the duplicate BCF3010 exists for, arriving at the one name BCF3010 must let through. That
    /// exception is what the channel buys: <c>.Class("a").Class("b")</c> is a single attribute, so the
    /// duplicate check cannot be asked about the name alone. It is asked about the channel instead — a
    /// binding is the third spelling that reaches <c>class</c> and the only one that does not fold, so it
    /// collides with every other decoration of the name and with nothing else (#188).
    /// </para>
    /// <para>
    /// Which of the two frames wins is left unsaid, because it is not one answer. Duplicate attributes in
    /// prerendered markup are resolved by the HTML parser, which keeps the first; an interactive render
    /// applies them through the DOM, where the last write stands. Reporting does not require settling it,
    /// and the shape has no reading under which both frames were wanted.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3024 = new(
        id: "BCF3024",
        title: "Class channel and a bound 'class' on one element",
        messageFormat: "'class' is bound with .Bind on an element that also decorates the class channel; the binding does not fold into it, so the element is emitted with the attribute twice; write every class through one of them",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "'class' is the one attribute a decoration may repeat, because .Class and .Attr(\"class\", …) " +
            "fold into a single space-joined attribute. A .Bind on the same name does not join that fold; " +
            "it emits its own attribute frame, so the element carries 'class' twice and which value " +
            "survives depends on how the component is rendered. Supply the whole class value from one " +
            "place: the binding's getter, or the .Class decorations without the binding.");

    /// <summary>
    /// The one diagnostic the content-slot surface needs (#34, #176). Everything else it has to refuse is
    /// refused by C#, because <c>SlotView</c> declares no conversion to <c>View</c>: a call whose brackets
    /// were forgotten is not a child, a decorated one finds no extension method, and the positional spelling
    /// has no parameter to bind. What the type system cannot see is a <c>Slot</c> in a body that has no
    /// caller content to place — a component's <c>Body</c> or <c>Chrome</c>, or a part returning <c>View</c>
    /// — and a part that names its slot other than exactly once.
    /// </summary>
    /// <remarks>
    /// Both halves are one descriptor because they are one mistake seen from two sides: a <c>Slot</c> whose
    /// enclosing declaration does not bind one. The message carries which side it was, so the report reads
    /// as the specific complaint rather than as the union of two. Zero and two are reported at the
    /// declaration and a misplaced one at the <c>Slot</c> itself, each being where the author can act.
    /// <para>
    /// The alternative was to let it fall through to BCF1003, which is what an unrecognized <c>View</c>-valued
    /// property already does. That reaches the author as "not a statically sequenceable expression", which
    /// names the mechanism and not the cause — the defect #191 was filed for.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3025 = new(
        id: "BCF3025",
        title: "Slot outside a content-taking [ViewPart] body",
        messageFormat: "'Slot' {0}; a slot exists only in the body of a [ViewPart] method declared to return SlotView, and exactly once there",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "'Slot' marks where a [ViewPart] part places the content its caller supplied in brackets, so " +
            "it means nothing where there is no caller content to place: a component's Body or Chrome " +
            "receives no brackets, and a part returning View is called without them. A part that takes " +
            "content declares SlotView as its return type and names Slot exactly once; naming it twice " +
            "would emit the caller's content twice, and not naming it at all would discard content the " +
            "caller was required to supply.");

    /// <summary>
    /// BCF3026: a name written in a decoration's position that <c>BlazorCodeFirst.Decorations</c> does not
    /// declare, on a receiver that does open an element frame.
    /// </summary>
    /// <remarks>
    /// The complement of BCF3008 over the same sweep. BCF3008's condition is about the receiver and this
    /// one's is about the name, and <see cref="Analysis.KnownSymbols.DeclaresDecorationNamed"/> makes them
    /// disjoint: a name the runtime declares can only be misplaced, and a name it does not declare is
    /// unrecognized wherever it stands. Without this descriptor both shapes ended at BCF1003, whose message
    /// says the expression "uses a construct that is not statically analyzable". That is untrue here. The
    /// receiver opens an element frame and the children are ordinary; only the name is unrecognized (#241).
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3026 = new(
        id: "BCF3026",
        title: "Decoration name is not one this generator recognizes",
        messageFormat: "'{0}' is not a decoration this generator recognizes; BlazorCodeFirst.Decorations declares no such name",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A decoration must be one of the extension methods BlazorCodeFirst.Decorations declares. A name " +
            "it does not declare is not translated, whether it fails to bind at all or binds to some other " +
            "method that happens to take an element and return one.");

    /// <summary>
    /// BCF3027: an element written as a simple name that a member declared closer than
    /// <c>BlazorCodeFirst.Html</c> shadows, so the brackets index that member instead of opening an element.
    /// </summary>
    /// <remarks>
    /// The C# error here is CS1503 on the index argument, which names neither the element, nor the member
    /// that took its place, nor the fix, and which the declaration-stage cutoff keeps from the author in any
    /// case (付録A A.0). Without this descriptor the author was told the expression "uses a construct that is
    /// not statically analyzable", which is untrue: the construct is ordinary and the lookup went elsewhere
    /// (#127). A <em>type</em> that shadows a helper is not covered, which is a gap and no longer a position:
    /// the CS0119 that #127 credited with naming the shadowing declaration does not reach the author either
    /// (#266).
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3027 = new(
        id: "BCF3027",
        title: "Element helper is shadowed by a member of your own",
        messageFormat: "'{0}' here is a member declared outside BlazorCodeFirst.Html, not the element helper of that name; write 'Html.{0}' to name the element",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "'using static BlazorCodeFirst.Html;' brings every curated element helper into simple-name " +
            "scope, and a member declared closer wins that lookup. Where that member's own type is " +
            "indexable the element expression stays legal C# and quietly becomes an indexer call on the " +
            "member. Qualify the element as Html.<Name> to name it past the member.");

    /// <summary>
    /// BCF3028: an event handler whose argument type is not one the named event can deliver — either it
    /// disagrees with the event's <c>[EventHandler]</c> mapping, or it is not a <c>System.EventArgs</c> at
    /// all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One descriptor for two shapes, with the reason carried in the message the way BCF1002 carries its
    /// own (#155). To an author they are one mistake, naming the wrong argument type for an event, and the
    /// fix is the same either way; two ids would split it by a distinction the author never made, namely
    /// whether C# managed to bind the call.
    /// </para>
    /// <para>
    /// The mismatch binds, so it is reported from the decoration arm where both sides are already in hand.
    /// A <c>TArgs</c> outside the <c>where TArgs : System.EventArgs</c> constraint never binds, so it comes
    /// from the failure-path sweep, the position BCF3008 reports from and for the same reason: the C# error
    /// that would explain it (CS0311) is computed for a compilation the declaration-stage cutoff has already
    /// abandoned, so the author was left with BCF1003's "not statically analyzable" — measured, #155.
    /// </para>
    /// <para>
    /// The test is assignability rather than equality, because an <c>EventCallback&lt;TArgs&gt;</c> handed
    /// the event's own argument object casts it to <c>TArgs</c>: a base type receives it and a sibling type
    /// does not. Razor performs this check from the same metadata, so leaving it unreported would put this
    /// surface behind Razor on a check Razor performs (<c>DESIGN.md</c> §4.1).
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3028 = new(
        id: "BCF3028",
        title: "Event handler cannot receive the event's arguments",
        messageFormat: "The event handler cannot receive '{0}': {1}",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Blazor dispatches an event by casting its argument object to the handler's argument type, so " +
            "that type has to be one the named event delivers, or a base of it. The mapping comes from the " +
            "[EventHandler] metadata the framework ships and from any registration in the compilation " +
            "being built; an event with no entry has no mapping and is not checked. A type that is not a " +
            "System.EventArgs at all is outside the constraint the decoration declares and no event can " +
            "deliver it.");

    /// <summary>
    /// BCF3029: an expression of the design-time API written where no design-time expression reads it, so
    /// it builds an empty marker, renders nothing, and wires up no handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The edge the inert-<c>View</c> model leaves exposed: the same API is callable from anywhere and
    /// means nothing outside the three positions the generator reads. It type-checks, it looks like it
    /// built something, and the only symptom is missing output (#68).
    /// </para>
    /// <para>
    /// Those three positions — a component's <c>Body</c>, a layout's <c>Chrome</c>, and a
    /// <c>[ViewPart]</c> body — are recognized by the enclosing declaration returning an inert type, never
    /// by a list of positions. <c>FailurePathScanners</c>' remarks record what a check costs once its host
    /// set becomes something a human enumerates (#100), and this one is not to begin with that shape.
    /// </para>
    /// <para>
    /// Storage is deliberately outside it. <c>ARCHITECTURE.md</c> §2.3 classifies the design-time API's
    /// <em>calls</em>, not what an author does with the value, so caching one in an inert-typed field or
    /// property stays unreserved and an Error is the wrong instrument for closing a door the design may
    /// later want open. A local is not that: it dies with its declaration, and one that is returned or
    /// captured has already been exempted by the declaration it is returned from.
    /// </para>
    /// <para>
    /// A <c>View</c>-returning declaration of the author's own is not this diagnostic's business either,
    /// and for a stronger reason: it is the Opaque path <c>DESIGN.md</c> §5.3 reserves, whose spelling
    /// 付録B.11(b) refuses to erase, and the forgotten <c>[ViewPart]</c> that reaches it by accident is
    /// answered by BCF2001 at Info (#260). Only members of the design-time API itself are reported, which
    /// is what <c>KnownSymbols.IsDesignTimeApiMember</c> decides.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3029 = new(
        id: "BCF3029",
        title: "Design-time syntax outside a design-time expression renders nothing",
        messageFormat:
            "This design-time syntax is never read here, so it renders no output and wires up no event "
                + "handler; write it in a Body, a Chrome, or a [ViewPart] method",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every factory and decoration in the design-time API is inert: View is an empty struct, an "
                + "element helper returns default, and a decoration returns its receiver unchanged. The "
                + "generator reads the syntax, never the value, and it reads it in exactly three places: a "
                + "component's Body, a layout's Chrome, and the body of a [ViewPart] method. Written "
                + "anywhere else — a service, a helper method, an event handler — the expression compiles, "
                + "returns the empty marker, renders nothing, and leaves every handler in it unwired. "
                + "Storing such a value in a field or property of a design-time type is not reported; only "
                + "calls are reserved, so the stored form is left open.");

    /// <summary>
    /// Every declared descriptor, discovered reflectively from this type's public static
    /// <see cref="DiagnosticDescriptor"/> fields so a newly added descriptor registers automatically and
    /// <see cref="ById"/> cannot drift out of sync. Declared after the descriptor fields so their static
    /// initializers have already run when this map is built (static field initializers run in textual order).
    /// </summary>
    private static readonly ImmutableDictionary<string, DiagnosticDescriptor> ByIdMap =
        typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(static field => (DiagnosticDescriptor)field.GetValue(null)!)
            .ToImmutableDictionary(static descriptor => descriptor.Id, StringComparer.Ordinal);

    /// <summary>Resolves a captured diagnostic <paramref name="id"/> to its descriptor.</summary>
    public static DiagnosticDescriptor ById(string id) =>
        ByIdMap.TryGetValue(id, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown BlazorCodeFirst diagnostic id.");
}

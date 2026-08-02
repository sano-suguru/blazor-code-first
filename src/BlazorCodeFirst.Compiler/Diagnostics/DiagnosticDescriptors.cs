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
            "declaring the override — an intermediate abstract base, a leaf whose base already declares " +
            "it, or a re-abstraction — has nothing generated into it and needs no partial modifier.");

    /// <summary>
    /// BCF1002: A <c>[Composable]</c> method does not satisfy the source generator's supported
    /// static-expansion contract.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF1002 = new(
        id: "BCF1002",
        title: "Composable method shape is unsupported",
        messageFormat: "Composable method '{0}' is unsupported: {1}",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A method marked Composable must satisfy the compiler's supported static expansion contract.");

    /// <summary>
    /// BCF3001: A BlazorCodeFirst base's design-time expression getter (<c>Body</c> on <c>BodyComponentBase</c>,
    /// <c>Chrome</c> on <c>ChromeLayoutBase</c>) must not mutate component state (single-direction
    /// data-flow violation).
    /// The initial detectable boundary covers statically identifiable direct writes (field assignments,
    /// property assignments, and increment/decrement operators) whose target is an instance member of
    /// the containing component.  Recognized deferred event handler lambdas (the <c>Button</c> onClick
    /// argument) are excluded.  Arbitrary interprocedural side effects are not guaranteed to be detected.
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
            "root is a region (a bare If or nested ForEach, or a composable whose body is region-rooted) " +
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
    /// BCF1004: A design-time expression override declares a getter the generator cannot translate —
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
            "expression. A getter that contains statements — for example a local variable declared before " +
            "the return — would require the Transplantable path, which is not implemented. An auto " +
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
    /// BCF3005: A <c>Component&lt;T&gt;().Param</c> selector is not a simple property selection on its own
    /// lambda parameter (for example <c>c =&gt; c.Label</c>). Casts, method calls, null-conditional access,
    /// or a member of a captured variable cannot be turned into a static parameter setter.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3005 = new(
        id: "BCF3005",
        title: "Component parameter selector must be a simple property selection",
        messageFormat: "Component parameter selector must select a property of the lambda parameter, such as c => c.Label",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>().Param takes a selector of the form c => c.Property so the source generator can " +
            "emit a static parameter setter. A cast, method call, null-conditional access, or a member of a " +
            "captured variable cannot be statically resolved to a parameter name.");

    /// <summary>
    /// BCF3006: A <c>Component&lt;T&gt;().Param</c> target is not a settable <c>[Parameter]</c> property.
    /// Setting a non-parameter (or a parameter with no accessible setter) would throw at runtime, so it is
    /// rejected at compile time.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3006 = new(
        id: "BCF3006",
        title: "Component parameter target must be a settable [Parameter] property",
        messageFormat: "'{0}' is not a settable [Parameter] property; Param can only bind properties marked [Parameter] with an accessible setter",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>().Param can only bind a property marked [Parameter] with a public setter. Binding " +
            "any other member would throw at runtime when Blazor applies the parameters.");

    /// <summary>
    /// BCF3007: A <c>Component&lt;T&gt;().Param</c> chain binds the same property more than once. Blazor
    /// silently applies the last binding, so a duplicate is almost certainly a mistake; it is rejected at
    /// compile time rather than allowed to shadow a value at runtime.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3007 = new(
        id: "BCF3007",
        title: "Component parameter is bound more than once",
        messageFormat: "'{0}' is bound more than once; remove the duplicate .Param(...) call",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>().Param must bind each parameter at most once per chain. Binding the same " +
            "property twice makes the earlier value dead — Blazor applies the last write — so the " +
            "duplicate is reported at compile time.");

    /// <summary>
    /// BCF3008: A decoration (<c>.Class</c>/<c>.Attr</c>/a named attribute shortcut/<c>.OnClick</c>/<c>.On</c>)
    /// was applied to a node that does not open a single HTML element (an <c>If</c>/<c>ForEach</c> region
    /// root, a <c>Fragment</c>/<c>Raw</c> wrapper-less node, or a <c>[Composable]</c>/Component call result).
    /// Decorations fold into the attributes of the element opened by an Html element helper or
    /// <c>Html.Element</c>, so there must be such an element to attach to.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3008 = new(
        id: "BCF3008",
        title: "Decoration target must be a single element",
        messageFormat: "A decoration can only be applied to a single element (an Html element such as Div/Span/Button, or Html.Element); it cannot be applied to If, ForEach, Fragment, Raw, or a [Composable]/Component result",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A decoration folds into the owning element's attributes, so it can only be applied to a node " +
            "that opens a single HTML element (an Html element helper or Html.Element). Applying it to a " +
            "region-rooted or wrapper-less node (If, ForEach, Fragment, Raw) or a " +
            "[Composable]/Component result has no element to attach to. Decorate a concrete element instead.");

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
    /// inline handler and a C# handler each fire on every event. <c>class</c> is the sole exception —
    /// multiple <c>.Class</c>/<c>.Attr("class", …)</c> fold into one space-joined attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3010 = new(
        id: "BCF3010",
        title: "Attribute or event is bound more than once",
        messageFormat: "'{0}' is bound more than once on this element; remove the duplicate (only 'class' may be repeated — it folds)",
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
    /// either fails with a CS0246 inside generated code — which the author cannot fix from their own
    /// file — or binds silently to a different same-named type that happens to be reachable from the
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
    /// that can receive it — no <c>ChildContent</c> at all, one that is not a settable
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
    /// BCF3014: an inert design-time value (<c>View</c>, <c>ElementBuilder</c>, or
    /// <c>ComponentView&lt;T&gt;</c>) was passed to the generic <c>Param</c>, whose value is emitted
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

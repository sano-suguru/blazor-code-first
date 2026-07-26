using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Diagnostics;

internal static class DiagnosticDescriptors
{
    /// <summary>
    /// BC1001: A class deriving from <c>ComposeComponentBase</c> must be declared <c>partial</c>
    /// so the source generator can emit the <c>RenderView</c> override.
    /// </summary>
    public static readonly DiagnosticDescriptor BC1001 = new(
        id: "BC1001",
        title: "ComposeComponentBase subclass must be partial",
        messageFormat: "'{0}' derives from ComposeComponentBase but is not declared partial; add the partial modifier",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Classes that derive from ComposeComponentBase must be declared partial so the source generator can emit the RenderView override.");

    /// <summary>
    /// BC1002: A <c>[Composable]</c> method does not satisfy the source generator's supported
    /// static-expansion contract.
    /// </summary>
    public static readonly DiagnosticDescriptor BC1002 = new(
        id: "BC1002",
        title: "Composable method shape is unsupported",
        messageFormat: "Composable method '{0}' is unsupported: {1}",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A method marked Composable must satisfy the compiler's supported static expansion contract.");

    /// <summary>
    /// BC3001: A <c>Body</c> getter must not mutate component state (single-direction
    /// data-flow violation).
    /// The initial detectable boundary covers statically identifiable direct writes (field assignments,
    /// property assignments, and increment/decrement operators) whose target is an instance member of
    /// the containing component.  Recognized deferred event handler lambdas (the <c>Button</c> onClick
    /// argument) are excluded.  Arbitrary interprocedural side effects are not guaranteed to be detected.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3001 = new(
        id: "BC3001",
        title: "State mutation inside Body violates single-direction data flow",
        messageFormat: "'{0}' is mutated inside Body; move state changes to event handlers to preserve single-direction data flow",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The Body getter must be a pure projection of state to UI. " +
            "Mutating component state inside Body causes render-time side effects that can corrupt " +
            "the rendering pipeline. Move the mutation to an event handler.");

    /// <summary>
    /// BC3002: A <c>ForEach</c> key selector does not reference its item and therefore cannot express
    /// per-item identity, defeating keyed diffing. Heuristic and intentionally conservative: it does not
    /// detect an item-derived-but-index-like key.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3002 = new(
        id: "BC3002",
        title: "ForEach key selector may not identify items",
        messageFormat: "ForEach key selector does not reference the item; a key must identify each item so list state is preserved across reorders",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A ForEach key selector should return a value derived from the item so Blazor can preserve " +
            "per-row state across insertion, removal, and reordering. A key that ignores the item " +
            "(a constant, an external index, or another list's item) forces full re-rendering.");

    /// <summary>
    /// BC3003: A <c>ForEach</c> content template's root is not a single element or component, so its key
    /// has no frame to attach to (Blazor's <c>SetKey</c> keys the currently open element/component frame).
    /// The required-key contract cannot be honored, so emission is suppressed. Mirrors Razor, where
    /// <c>@key</c> cannot be applied to an <c>@if</c>; wrap the content in an element such as
    /// <c>Html.Div(...)</c> instead.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3003 = new(
        id: "BC3003",
        title: "ForEach content must be a single element or component",
        messageFormat: "ForEach content must be a single element or component so its key can be applied; wrap it in a container such as Html.Div(...)",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A ForEach key is applied to the content's root element or component frame. When the content " +
            "root is a region (a bare If or nested ForEach, or a composable whose body is region-rooted) " +
            "or bare text (a plain string value with no wrapping element), there is no frame to key, so the " +
            "required key cannot be applied. A Fragment (wrapper-less grouping) and Raw (raw markup) also " +
            "open no single keyable frame; make the content a single element or component. Wrap the content " +
            "in a container element such as Html.Div(...).");

    /// <summary>
    /// BC1003: A component <c>Body</c> reached the model stage but could not be translated to a RenderView
    /// (no template, and no other actionable diagnostic was produced). Explains the CS0534 that the abstract
    /// RenderView would otherwise raise on its own. Transitional: its firing condition shrinks once the
    /// Opaque/Transplantable fallback paths are implemented.
    /// </summary>
    public static readonly DiagnosticDescriptor BC1003 = new(
        id: "BC1003",
        title: "Component Body could not be translated",
        messageFormat: "Component '{0}' Body could not be translated to a RenderView; it uses a construct that is not statically analyzable",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The Body expression could not be classified into the statically sequenceable subset and no " +
            "runtime fallback exists yet, so no RenderView is generated. Use the supported factories and " +
            "combinators, or an inline expression lambda, so the body can be analyzed.");

    /// <summary>
    /// BC3004: A <c>ForEach</c> content or key is not an inline expression lambda (for example a block-bodied
    /// lambda or a method group), so it cannot be statically analyzed. Transitional: narrows once the
    /// Transplantable/Opaque paths support such content.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3004 = new(
        id: "BC3004",
        title: "ForEach content must be an inline expression lambda",
        messageFormat: "ForEach content and key must be inline expression lambdas so they can be statically analyzed; wrap the call in a lambda such as x => Wrapper(x)",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A ForEach content or key selector must be an inline expression lambda (item => ...). A " +
            "block-bodied lambda or a method group cannot be statically sequenced. Rewrite it as an inline " +
            "expression lambda, wrapping any helper call as x => Helper(x).");

    /// <summary>
    /// BC3005: A <c>Component&lt;T&gt;().Param</c> selector is not a simple property selection on its own
    /// lambda parameter (for example <c>c =&gt; c.Label</c>). Casts, method calls, null-conditional access,
    /// or a member of a captured variable cannot be turned into a static parameter setter.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3005 = new(
        id: "BC3005",
        title: "Component parameter selector must be a simple property selection",
        messageFormat: "Component parameter selector must select a property of the lambda parameter, such as c => c.Label",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>().Param takes a selector of the form c => c.Property so the source generator can " +
            "emit a static parameter setter. A cast, method call, null-conditional access, or a member of a " +
            "captured variable cannot be statically resolved to a parameter name.");

    /// <summary>
    /// BC3006: A <c>Component&lt;T&gt;().Param</c> target is not a settable <c>[Parameter]</c> property.
    /// Setting a non-parameter (or a parameter with no accessible setter) would throw at runtime, so it is
    /// rejected at compile time.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3006 = new(
        id: "BC3006",
        title: "Component parameter target must be a settable [Parameter] property",
        messageFormat: "'{0}' is not a settable [Parameter] property; Param can only bind properties marked [Parameter] with an accessible setter",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>().Param can only bind a property marked [Parameter] with a public setter. Binding " +
            "any other member would throw at runtime when Blazor applies the parameters.");

    /// <summary>
    /// BC3007: A <c>Component&lt;T&gt;().Param</c> chain binds the same property more than once. Blazor
    /// silently applies the last binding, so a duplicate is almost certainly a mistake; it is rejected at
    /// compile time rather than allowed to shadow a value at runtime.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3007 = new(
        id: "BC3007",
        title: "Component parameter is bound more than once",
        messageFormat: "'{0}' is bound more than once; remove the duplicate .Param(...) call",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>().Param must bind each parameter at most once per chain. Binding the same " +
            "property twice makes the earlier value dead — Blazor applies the last write — so the " +
            "duplicate is reported at compile time.");

    /// <summary>
    /// BC3008: A decoration (<c>.Class</c>/<c>.Attr</c>/a named attribute shortcut/<c>.OnClick</c>/<c>.On</c>)
    /// was applied to a node that does not open a single HTML element (an <c>If</c>/<c>ForEach</c> region
    /// root, a <c>Fragment</c>/<c>Raw</c> wrapper-less node, or a <c>[Composable]</c>/Component call result).
    /// Decorations fold into the attributes of the element opened by an Html element helper or
    /// <c>Html.Element</c>, so there must be such an element to attach to.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3008 = new(
        id: "BC3008",
        title: "Decoration target must be a single element",
        messageFormat: "A decoration can only be applied to a single element (an Html element such as Div/Span/Button, or Html.Element); it cannot be applied to If, ForEach, Fragment, Raw, or a [Composable]/Component result",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A decoration folds into the owning element's attributes, so it can only be applied to a node " +
            "that opens a single HTML element (an Html element helper or Html.Element). Applying it to a " +
            "region-rooted or wrapper-less node (If, ForEach, Fragment, Raw) or a " +
            "[Composable]/Component result has no element to attach to. Decorate a concrete element instead.");

    /// <summary>
    /// BC3009: <c>Html.Element</c> was called with a tag argument that is not a non-empty compile-time
    /// constant string. A non-empty constant tag keeps the element declarative and predictable
    /// (design-time syntax the generator can lower to a literal <c>OpenElement</c>); this is not an
    /// AOT/sequencing constraint, and non-constant or empty tags are not a security (injection) concern.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3009 = new(
        id: "BC3009",
        title: "Element tag must be a compile-time constant string",
        messageFormat: "Html.Element tag must be a non-empty compile-time constant string; use a non-empty " +
            "string literal or a const",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Html.Element(tag, ...) lowers the tag to a literal OpenElement call, so the tag must be a " +
            "non-empty compile-time constant string. This keeps the vocabulary declarative and " +
            "predictable, consistent with the design-time nature of the factories.");

    /// <summary>
    /// BC3010: An attribute or event is bound more than once on the same element. Blazor applies the last
    /// binding and silently drops earlier ones, so a duplicate is dead code. <c>class</c> is the sole
    /// exception — multiple <c>.Class</c>/<c>.Attr("class", …)</c> fold into one space-joined attribute.
    /// </summary>
    public static readonly DiagnosticDescriptor BC3010 = new(
        id: "BC3010",
        title: "Attribute or event is bound more than once",
        messageFormat: "'{0}' is bound more than once on this element; remove the duplicate (only 'class' may be repeated — it folds)",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every attribute and event other than 'class' is single-binding. Binding one twice makes the " +
            "earlier value dead — Blazor applies the last write — so the duplicate is reported at compile " +
            "time. Multiple .Class or .Attr(\"class\", …) decorations fold into a single class attribute.");

    /// <summary>
    /// BC3011: A <c>.Attr</c> name or <c>.On</c> event name is not a non-empty compile-time constant
    /// string. A constant name keeps the decoration declarative and typo-checkable, and is required to
    /// route class-folding and detect duplicate bindings. Sibling of BC3009 (constant Element tag).
    /// </summary>
    public static readonly DiagnosticDescriptor BC3011 = new(
        id: "BC3011",
        title: "Attribute/event name must be a compile-time constant string",
        messageFormat: ".Attr name and .On event name must be a non-empty compile-time constant string",
        category: "BlazorCompose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            ".Attr(name, …) and .On(eventName, …) lower the name to a literal attribute, so it must be a " +
            "non-empty compile-time constant string. This keeps the vocabulary declarative and " +
            "typo-checkable and is the prerequisite for class folding and duplicate-binding detection.");

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
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown BlazorCompose diagnostic id.");
}

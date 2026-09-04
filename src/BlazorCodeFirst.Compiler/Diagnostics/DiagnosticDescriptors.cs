using System.Collections.Immutable;
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
    /// static-expansion contract, or a component's own design-time expression references something that
    /// cannot exist in generated code.
    /// </summary>
    /// <remarks>
    /// The subject is an argument rather than part of the format because two positions report this: a
    /// <c>[ViewPart]</c> declaration or one of its call sites, and a component's own design-time
    /// expression, which normalizes its body through the same check and is not a method (#361). Callers
    /// build it with <see cref="ViewPartSubject"/> or <see cref="DesignTimeExpressionSubject"/> so the two
    /// wordings live in one place. The title names both by what they have in common.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF1002 = new(
        id: "BCF1002",
        title: "Statically expanded body is unsupported",
        messageFormat: "{0} is unsupported: {1}",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A method marked [ViewPart] must satisfy the compiler's supported static expansion contract, " +
            "and a component's design-time expression must reference only what generated code can name.");

    /// <summary>BCF1002's subject for a <c>[ViewPart]</c> declaration or one of its call sites.</summary>
    public static string ViewPartSubject(string methodName) => $"ViewPart method '{methodName}'";

    /// <summary>
    /// The subject BCF1002 and BCF1003 share for a component's own design-time expression. One spelling
    /// rather than two formats, because an author who meets both is looking at one property. Argument
    /// order follows the two diagnostics' own, type before expression.
    /// </summary>
    public static string DesignTimeExpressionSubject(string typeName, string expressionName) =>
        $"The {expressionName} design-time expression of '{typeName}'";

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
        messageFormat: "ForEach content must be a single element or component so its key can be applied",
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
        messageFormat: "{0} could not be translated to a RenderView; it uses a construct that is not statically analyzable",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The design-time expression could not be classified into the statically sequenceable subset " +
            "and no runtime fallback exists yet, so no RenderView is generated. Use the supported " +
            "element helpers, combinators, Component<T>(), Fragment, Raw, an inline expression " +
            "lambda, or a call to a method marked [ViewPart], so the expression can be analyzed. " +
            "Marking a View-returning method of your own [ViewPart] keeps the factoring rather than " +
            "inlining its markup back into the caller.");

    /// <summary>
    /// BCF1004: A design-time expression override declares a getter the generator cannot translate,
    /// either a getter body outside the shapes <see cref="Analysis.RenderExpressionAnalyzer"/> accepts, or
    /// an auto property, which declares no getter body at all. Distinct from BCF1003: the getter's shape is
    /// the problem, not the constructs used inside it, and the fix is to rewrite the getter rather than to change which
    /// element helpers are read. Reported at the property identifier rather than inside the getter, which is
    /// the same distinction: BCF1003 blames an expression, BCF1004 blames the declaration around it. Not
    /// reported when the component overrides <c>RenderView</c> by hand (the design-time expression is
    /// then unused and the code is correct), nor for a partial property with no implementation part
    /// (CS9248 already names it).
    /// </summary>
    public static readonly DiagnosticDescriptor BCF1004 = new(
        id: "BCF1004",
        title: "Design-time expression getter must reach a single returned expression",
        messageFormat: "'{0}' declares the {1} design-time expression with a getter that does not reach a single returned expression; write it as '=> expr', 'get => expr', or a getter block of local declarations and expression statements ending in one 'return expr;'",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A design-time expression is an inert projection of state to UI that the generator translates " +
            "statically; it is never evaluated at runtime. Its getter must therefore reach one returned " +
            "expression. Local declarations and expression statements may precede that return and are " +
            "transplanted into the generated RenderView ahead of the frames. A second return and native " +
            "control flow each need a sequence space of their own, so neither is accepted; a local " +
            "spelled with a name the generator reserves is refused rather than renamed. An auto property " +
            "declares no getter to translate at all. Supply RenderView by hand if the body cannot be " +
            "written in this shape.");

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
    /// BCF1006: a <c>static ElementView</c> property referenced as an element tag alias (#173) is declared
    /// in a referenced assembly, so its declaration syntax is not visible to resolve. Same-compilation-only
    /// constraint, the same shape BCF1002 already applies to a <c>[ViewPart]</c> call site.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF1006 = new(
        id: "BCF1006",
        title: "Element tag alias is declared outside this compilation",
        messageFormat: "'{0}' cannot be resolved as an element tag alias because its declaration is not in this compilation",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A static ElementView property used as an element tag alias must be declared in the current " +
            "compilation: the generator resolves its tag by reading the declaration's own syntax, which a " +
            "referenced assembly does not carry.");

    /// <summary>
    /// BCF2001: A call the generator cannot expand statically. It becomes a dynamic region and the static
    /// diff optimization for that area is lost.
    /// </summary>
    /// <remarks>
    /// Info, not a warning: the call is correct and renders correctly, and Appendix A assigns this ID to the
    /// lost optimization alone. The case where the call renders <em>nothing</em> is
    /// <see cref="BCF3030"/>, which is a different fact and carries an error.
    /// <para>
    /// One residue this cannot see. A referenced assembly's <c>View</c>-returning method may be built from
    /// the design-time surface, in which case it carries no fragment and renders nothing, and no source
    /// declaration exists here to tell. Appendix A's row records it; <c>DESIGN.md</c> §4.3 already routes
    /// cross-assembly reuse to components.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF2001 = new(
        id: "BCF2001",
        title: "Opaque call degrades to a dynamic region",
        messageFormat:
            "'{0}' cannot be expanded statically, so this area renders through a runtime fragment and "
                + "loses its static diff optimization",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
            "The generator expands what it can read: the design-time surface, and [ViewPart] methods "
                + "declared in this compilation. A call it cannot read is rendered at runtime through the "
                + "RenderFragment the returned View carries, inside a region that keeps its sequence "
                + "numbers away from the rest of the component. Correctness is unaffected; the frames for "
                + "that area are rebuilt rather than diffed against a static template.");

    /// <summary>
    /// BCF2002: A native `if`/`else` or `switch` transplanted into a Body/Chrome getter, a ForEach content
    /// lambda, or a [ViewPart] body degrades to a dynamic region; each arm/section's content is drawn
    /// through a runtime fragment and loses its static diff optimization.
    /// </summary>
    /// <remarks>
    /// Info, not a warning, for the same reason as BCF2001: the chain is correct and renders correctly,
    /// only the static-diff optimization is lost. Reported once per `if`/`else` chain or `switch`, at its
    /// outermost `if`'s condition or the `switch`'s discriminant, regardless of how many `else if` links
    /// or `case` sections it holds.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF2002 = new(
        id: "BCF2002",
        title: "Native if/else or switch degrades to a dynamic region",
        messageFormat:
            "This native branch cannot be statically assigned, so each arm or section renders through a "
                + "runtime fragment and loses its static diff optimization",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:
            "A native `if`/`else` or `switch` transplanted whole is wrapped in a region whose boundary "
                + "sequence is fixed to syntactic position, same as If()'s own region. Unlike If(), each "
                + "arm's/section's content is drawn through a freshly synthesized RenderFragment rather "
                + "than given its own static sequence range, because only one ever runs and no static "
                + "width can be assigned to content that might not execute. Correctness is unaffected; "
                + "the frames for whichever arm/section runs are rebuilt rather than diffed against a "
                + "static template.");

    /// <summary>
    /// BCF3004: A <c>ForEach</c> key is not an inline expression lambda, or its content is a shape the
    /// generator neither sequences statically nor transplants.
    /// </summary>
    /// <remarks>
    /// Narrowed when the Transplantable and Opaque paths landed. Content now also accepts a block with one
    /// trailing <c>return</c> (ARCHITECTURE.md §2.3 Transplantable) and a one-parameter
    /// <c>View</c>-returning method group, which is read as the call it stands for and answered by the
    /// same three-way split every other call gets. What is left is the key, whose body has to be an
    /// expression because it is transplanted into <c>SetKey</c>, and the content shapes that would each
    /// need a sequence space of their own.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3004 = new(
        id: "BCF3004",
        title: "ForEach key or content has a shape the generator cannot sequence",
        messageFormat:
            "ForEach requires an expression-bodied key lambda, and content that is an expression lambda, "
                + "a block with one trailing return, or a single-parameter method group",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The key body is transplanted into SetKey, so it has to be an expression. The content is "
                + "given one static sequence space that every iteration reuses, which a second return or a "
                + "native control statement would each need their own copy of.");

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
    /// BCF3009: <c>Html.Element</c> was called with a tag argument that is not a compile-time constant
    /// string, or with one no element can be named. The constant half keeps the element declarative and
    /// predictable (design-time syntax the generator can lower to a literal <c>OpenElement</c>); this is
    /// not an AOT/sequencing constraint, and a non-constant tag is not a security (injection) concern.
    /// The spelling half is a translation break rather than a validity question, so it needs no content
    /// model and stays inside <c>DESIGN.md</c> §4.1's boundary (#394). Which characters, and why those:
    /// <see cref="Analysis.KnownSymbols.IsValidTagName"/>.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3009 = new(
        id: "BCF3009",
        title: "Element tag must be a compile-time constant string spelled like a tag name",
        messageFormat: "Html.Element tag must be a compile-time constant string spelled like a tag name: an " +
            "ASCII letter, then ASCII letters, digits, '-', '_' or '.'",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Html.Element(tag, ...) lowers the tag to a literal OpenElement call, so the tag must be a " +
            "compile-time constant string. This keeps the vocabulary declarative and predictable, " +
            "consistent with the design-time nature of the surface. The constant must also be spelled " +
            "like a tag name, because a tag no element can be named renders as two different things: " +
            "prerendering writes it into markup, where the HTML parser reinterprets it, while the " +
            "interactive path passes it to createElement, which rejects it and tears down the circuit.");

    /// <summary>
    /// BCF3010: An attribute or event is bound more than once on the same element. Neither outcome is what
    /// the author wrote: two bindings in the attribute channel leave the earlier one dead (the last write
    /// wins), while one name bound through the attribute channel and the event channel keeps both, so an
    /// inline handler and a C# handler each fire on every event. On an element, <c>class</c> is the sole
    /// exception, multiple <c>.Class</c>/<c>.Attr("class", …)</c> fold into one space-joined attribute —
    /// this diagnostic also fires on a <c>ComponentView&lt;TComponent&gt;</c> receiver (#314), where
    /// there is no fold at all and every name, <c>class</c> included, is single-binding.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3010 = new(
        id: "BCF3010",
        title: "Attribute or event is bound more than once",
        messageFormat: "'{0}' is bound more than once; remove the duplicate (on an element, 'class' may be repeated, because it folds there)",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every attribute and event other than an element's 'class' is single-binding. Two bindings " +
            "of one name in the attribute channel make the earlier value dead, because the last write " +
            "wins; a name bound once through the attribute channel and once through the event channel " +
            "keeps both, so an inline handler and a C# handler both fire. Neither is what the author " +
            "asked for, so the duplicate is reported at compile time. On an element, multiple .Class or " +
            ".Attr(\"class\", …) decorations fold into a single class attribute instead; a " +
            "ComponentView<TComponent> receiver has no such fold, so 'class' is single-binding there too.");

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
    /// <c>[Parameter]</c>, or one whose type is not a render fragment. Blazor would throw while applying
    /// parameters. Both fragment arities are accepted: a <c>RenderFragment&lt;TContext&gt;</c> takes the
    /// children with the context discarded.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3013 = new(
        id: "BCF3013",
        title: "Component cannot receive child content",
        messageFormat:
            "'{0}' has no settable [Parameter] named 'ChildContent' of a RenderFragment type, so it cannot "
                + "receive child content; bind a fragment parameter by name with .Param(c => c.Name, content) "
                + "or .Template(c => c.Name, content) instead",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Component<T>()[children] binds child content to a parameter named ChildContent, mirroring how "
                + "Razor lowers nested content. The parameter must be a settable [Parameter] typed "
                + "RenderFragment or RenderFragment<TContext>; the generic one receives the children with "
                + "its context discarded. Without such a parameter Blazor throws while applying parameters, "
                + "so it is rejected at compile time.");

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
    /// apply. The number follows BCF3021, which was withdrawn (Appendix B.5) and stays retired.
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
    /// BCF3023: a decoration written on the <c>class</c> name carries a value the class channel cannot join
    /// as text, which is any value that is not a <see cref="string"/>. That name folds into the channel,
    /// which joins its decorations into one value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The condition is the channel's requirement, not a list of the overloads that fail it
    /// (<c>ClassChannel.Admit</c> asks whether the resolved overload's value is a <see cref="string"/> and
    /// refuses everything else, #193). Today the <see langword="bool"/> overload and the bare
    /// <c>.Attr("class")</c> spelling are the only ways to reach it, because <see cref="string"/> and
    /// <see langword="bool"/> are the only value types <c>.Attr</c> takes (<c>DESIGN.md</c> §4.1, #158). An
    /// overload added later reaches it without the analyzer being touched, which is what the allow-list is
    /// for, and the message names the type it found rather than assuming the one that made the rule
    /// reachable (#223).
    /// </para>
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
    /// <c>.Attr("class")</c> reaches the same rule without the author writing a value anywhere: the bare
    /// spelling stands for a presence, and a presence has no text (#178). It is reported at the
    /// decoration's name, there being no value argument to point at, and the message names the spelling
    /// rather than the <see langword="bool"/> that spelling is synthesized into — that constant is the
    /// compiler's, not the author's.
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
        messageFormat: "'class' folds into the class channel, which joins its values as text, so {0} has no meaning there; write the class as a string, .Class(\"name\") or .Class(condition ? \"name\" : null) for a conditional one",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The class channel joins the decorations written on 'class' into one value as text, so it " +
            "takes a string and nothing else. The bool overload of .Attr is Blazor's conditional-attribute " +
            "form and the bare .Attr(name) spelling stands for a presence; neither is text. Use .Class or " +
            "the string overload of .Attr.");

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
    /// BCF3027: an element written as a simple name that a declaration closer than
    /// <c>BlazorCodeFirst.Html</c> or <c>BlazorCodeFirst.Svg</c> took — a member, a type, a namespace, or a
    /// method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each shape has a C# error of its own, and the declaration-stage cutoff keeps every one of them from
    /// the author (Appendix A A.0): CS1503 on the index argument for a member, whose indexer the brackets quietly
    /// call; CS0119 for a type; CS0118 for a namespace; CS0021 for a method group. Without this descriptor
    /// the author was told the expression "uses a construct that is not statically analyzable", which is
    /// untrue — the construct is ordinary and the lookup went elsewhere (#127).
    /// </para>
    /// <para>
    /// One id for the four, with what took the name carried in the message the way BCF3028 carries its two
    /// shapes. To an author they are one mistake, a simple name that reached something nearer than
    /// <c>Html</c> or <c>Svg</c>, and qualifying the element is the fix for all of them; splitting them
    /// would split by how far C# got in binding the expression, which is a distinction the author never
    /// made. #127 covered the member alone on the premise that CS0119 reaches the type case, and #266
    /// measured that premise and found it false.
    /// </para>
    /// <para>
    /// The fix text is computed rather than a fixed "Html.{0}" (#319 / #534): a name curated in only one
    /// vocabulary gets that vocabulary's qualified spelling, and a name curated in both (<c>A</c>,
    /// <c>Audio</c>, <c>Canvas</c>, <c>Iframe</c>, <c>Video</c>) gets both, since the shadowed lookup alone
    /// cannot say which one the author meant — the shadowing declaration wins over every static import
    /// equally, regardless of how many bring the name into scope.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3027 = new(
        id: "BCF3027",
        title: "Element helper is shadowed by a declaration of your own",
        messageFormat: "'{0}' here is a {1}, not the element helper of that name; write '{2}' to name the element",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "'using static BlazorCodeFirst.Html;' (or '...Svg;') brings every curated element helper into " +
            "simple-name scope, and a member, type, namespace, or method declared closer wins that lookup. " +
            "The element expression then indexes that declaration, or fails to bind against it, instead of " +
            "opening an element. Qualify the element (Html.<Name> or Svg.<Name>) to name it past the " +
            "declaration.");

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
    /// Appendix B.11(b) refuses to erase, and the forgotten <c>[ViewPart]</c> that reaches it by accident is
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
    /// BCF3030: A call to a <c>View</c>-returning method that is built from the design-time surface but
    /// carries no <c>[ViewPart]</c> renders nothing.
    /// </summary>
    /// <remarks>
    /// The sibling of BCF3029 on the other side of the call. BCF3029 reports design-time syntax written
    /// where nothing reads it; this reports a call whose callee wrote design-time syntax that nothing
    /// read. Both fail the same way — the value is the empty marker and no frames are emitted — and
    /// neither is a compile error, so the author sees correct-looking code that renders nothing.
    /// <para>
    /// <c>ARCHITECTURE.md</c> §2.3 classifies this call as Opaque and §3.2 says the Opaque path renders
    /// the fragment the returned <c>View</c> carries. It carries none: every member of <c>Html</c>,
    /// <c>ElementView</c> and <c>Decorations</c> returns the default value, so the only route into
    /// <c>View.Fragment</c> is the <c>RenderFragment</c> conversion. Letting this call take the Opaque
    /// path would turn Appendix B.11(c)'s "cost you always notice" into the cost you never notice, which is the
    /// trade that appendix refuses. Appendix B.11's closing note is revised to name this diagnostic.
    /// </para>
    /// <para>
    /// The predicate is "does the callee's body reference the design-time surface", not BCF1002's full
    /// static-expansion contract. A callee that references the surface but cannot be expanded is still
    /// reported here, and BCF1002 then names the exact contract violation at the declaration once the
    /// author adds the attribute. Running the contract check at every call site would restate BCF1002 in
    /// a second place for no better message.
    /// </para>
    /// <para>
    /// Two remedies, because the attribute does not fit every receiver: an instance method reaches this
    /// diagnostic too, and BCF1002 rejects a non-static <c>[ViewPart]</c>. The location is the whole call
    /// expression, which is what the author rewrites.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3030 = new(
        id: "BCF3030",
        title: "Call to a View-returning method without [ViewPart] renders nothing",
        messageFormat:
            "'{0}' builds its View from the design-time surface but carries no [ViewPart], so this call "
                + "renders nothing; mark it [ViewPart] if it is static, or make it a component",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The design-time surface is inert: Html's element helpers, ElementView's indexer and every "
                + "decoration return the default View, and the generator reads the syntax rather than the "
                + "value. It reads it in a Body, a Chrome, and the body of a [ViewPart] method. A "
                + "View-returning method without the attribute is none of those, so its result carries no "
                + "fragment and the call emits no frames. Marking the method [ViewPart] expands it into "
                + "the call site; making it a component gives it a Body the generator reads.");

    /// <summary>
    /// BCF3031: a <c>.Bind</c> writes a format for a value type the framework declares no format-taking
    /// converter for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>string format</c> overloads of <c>CreateBinder</c> and <c>BindConverter.FormatValue</c> exist for
    /// <c>DateTime</c>, <c>DateTimeOffset</c>, <c>DateOnly</c>, <c>TimeOnly</c> and their nullable forms
    /// only. A format on anything else leaves the generated file's own call unable to bind, and appendix
    /// A.0 is why that cannot be left to the C# error: it is raised inside generated code, which the
    /// author does not read.
    /// </para>
    /// <para>
    /// The admitted set is read from <c>EventCallbackFactoryBinderExtensions</c>'s metadata rather than
    /// enumerated here, which is the criterion <c>DESIGN.md</c> §4.1 states and the precedent BCF3028 set
    /// by reading <c>[EventHandler]</c>. The message names the type it found rather than assuming which
    /// one reached the rule, as BCF3023's does.
    /// </para>
    /// <para>
    /// The location is the format argument, because that is what the author rewrites or removes. The
    /// culture is not in question: every type this surface binds may carry one, and only the format is
    /// restricted.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3031 = new(
        id: "BCF3031",
        title: "Bind format is not supported for this value type",
        messageFormat:
            "'.Bind' writes a format for '{0}', which the framework declares no format-taking converter "
                + "for; drop the format, or format the value in the getter and parse it in an explicit "
                + "setter",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A format is passed straight through to BindConverter.FormatValue on the way out and to "
                + "EventCallbackFactoryBinderExtensions.CreateBinder on the way back. Both declare their "
                + "format-taking overloads for DateTime, DateTimeOffset, DateOnly, TimeOnly and their "
                + "nullable forms only, so a format written for any other type would leave the generated "
                + "file with a call that does not bind. The set is read from the framework's own "
                + "metadata, not enumerated by this compiler.");

    /// <summary>
    /// BCF3032: A keyed <c>ForEach</c> whose content root keys itself with <c>.Key</c>.
    /// </summary>
    /// <remarks>
    /// The loop applies its key to the content root's frame, and the root's own decoration applies to that
    /// same frame. Two <c>SetKey</c> calls on one frame, of which the second wins, so the key the author
    /// reads as authoritative depends on emission order rather than on anything at the call site.
    /// <para>
    /// Sibling of BCF3003 and resolved from the same walk: that one fires when the root can carry no key
    /// at all, this one when it already carries one. The two cannot both fire on one loop, because a root
    /// with nowhere to put a <c>SetKey</c> also has nowhere to write a <c>.Key</c>.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3032 = new(
        id: "BCF3032",
        title: "ForEach content root is keyed twice",
        messageFormat: "this ForEach applies a key to a content root that already writes its own .Key; remove one of the two",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A ForEach key is applied to the content root's element or component frame, which is the frame "
                + "a .Key on that root writes to as well. SetKey overwrites, so one of the two keys is "
                + "dead, and which one depends on the order the frames are emitted in. Key the root or key "
                + "the loop, not both.");

    /// <summary>
    /// BCF3034: A call-site <c>.RenderMode</c> on a component whose own declaration fixes its render mode.
    /// </summary>
    /// <remarks>
    /// The framework refuses the pair outright: <c>ComponentFactory</c> throws an
    /// <c>InvalidOperationException</c> naming the fixed mode when a type carrying a
    /// <c>RenderModeAttribute</c> also receives a caller-specified one. The attribute is a unary predicate
    /// on the component type and rides in metadata, so this is decidable at the call site for a referenced
    /// assembly's component as well as for one declared here.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3034 = new(
        id: "BCF3034",
        title: "Render mode is fixed by the component's declaration",
        messageFormat: "'{0}' declares [{1}], so its render mode is fixed and cannot be set here; remove the .RenderMode",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A component whose declaration carries a RenderModeAttribute has a fixed render mode. Supplying "
                + "one at the call site as well makes the framework throw when it instantiates the "
                + "component, so the call site is where it is stopped. The call-site form is for a "
                + "component that declares no mode of its own, which is the case it exists for: the same "
                + "component rendered interactively from one page and statically from another.");

    /// <summary>
    /// BCF3033: The same non-attribute frame decoration is written twice on one element or component.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from BCF3010, which asks the same question of the attribute and event channels. Those two
    /// are keyed by a name the author wrote, and this one is not: <c>.Key</c> and its siblings each occupy
    /// a channel that holds one value and has nothing to key on but the decoration itself. Folding them
    /// into BCF3010 would make one descriptor answer for two different rules and give its message a name
    /// argument that half its reports could not fill.
    /// </para>
    /// <para>
    /// All three break differently and none of them breaks visibly, which is the shared reason to refuse
    /// them (ARCHITECTURE.md §2.7(E)). <c>SetKey</c> writes into the open frame, so the second call
    /// overwrites the first. <c>AddComponentRenderMode</c> appends, and the renderer reads the first frame
    /// it finds, so there the <em>second</em> is the one that dies. A reference capture appends too, and
    /// both actions run.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3033 = new(
        id: "BCF3033",
        title: "Frame decoration is written more than once",
        messageFormat: "'{0}' is written more than once on this node; remove the duplicate",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A decoration that is not an attribute occupies a channel holding one value. Writing it twice "
                + "cannot do what the author asked, and which of the two survives depends on the "
                + "RenderTreeBuilder call the decoration lowers to rather than on anything visible at the "
                + "call site, so the duplicate is reported at compile time.");

    /// <summary>
    /// BCF3035: An event modifier written where no event precedes it on the element.
    /// </summary>
    /// <remarks>
    /// <c>.PreventDefault</c> and <c>.StopPropagation</c> attach to the event written before them, which is
    /// the only reading a chain offers: the decorations carry no event name of their own. Written with no
    /// event ahead of them they would emit an attribute whose name no handler on this element answers to,
    /// and the framework validates nothing there (measured), so nothing downstream would report it.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3035 = new(
        id: "BCF3035",
        title: "Event modifier has no event to modify",
        messageFormat: "'.{0}' has no event before it on this element; write it after the .On it modifies",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "An event modifier attaches to the event written before it on the same element. Written before "
                + "every event, or on an element carrying none, it would emit an attribute no handler "
                + "reads. Move it after the .On it belongs to.");

    /// <summary>
    /// BCF3036: The same event modifier is written twice for one event.
    /// </summary>
    /// <remarks>
    /// The defect BCF3033 reports for the non-attribute frame decorations, on a channel that holds one
    /// value for the same reason. A separate ID because BCF3033's entry is explicitly about the three
    /// decorations that are <em>not</em> attribute frames, and these two are: they lower to an ordinary
    /// <c>AddAttribute</c> whose name carries the event. Reported rather than resolved because one of the
    /// two is dead whichever way the model takes it, and which one is not visible at the call site.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3036 = new(
        id: "BCF3036",
        title: "Event modifier written twice for one event",
        messageFormat: "'.{0}' is already written for '{1}' on this element; remove one of the two",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "An event modifier occupies a channel holding one value per event. Writing it twice cannot do "
                + "what the author asked, so the duplicate is reported at compile time rather than "
                + "resolved silently.");

    /// <summary>
    /// BCF3038: An event modifier the event's own <c>[EventHandler]</c> registration disables.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Blazor gates each modifier per event through <c>EventHandlerAttribute</c>, and the renderer ignores
    /// an attribute the registration disabled. Without this check the surface emits it anyway and nothing
    /// downstream says so, which is one point where it would be worse than Razor — the argument BCF3028's
    /// own entry makes for existing.
    /// </para>
    /// <para>
    /// Located at the modifier's decoration name, as BCF3035 is, because that is what the author deletes.
    /// The event is correct code and stays.
    /// </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3038 = new(
        id: "BCF3038",
        title: "Event modifier disabled by the event's registration",
        messageFormat: "'{1}' disables '.{0}' in its [EventHandler] registration; remove the modifier",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "An event's [EventHandler] registration decides which modifiers it accepts. Emitting one the "
                + "registration disables produces an attribute the renderer ignores, so it is reported at "
                + "the call site instead, as Razor reports the same combination.");

    public static readonly DiagnosticDescriptor BCF3039 = new(
        id: "BCF3039",
        title: "FormName must not be a literal empty string or null",
        messageFormat: ".FormName's argument must not be a literal empty string or null; " +
            "AddNamedEvent throws for either at run time",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            ".FormName lowers to RenderTreeBuilder.AddNamedEvent(\"onsubmit\", name), which throws " +
            "ArgumentException for an empty name and ArgumentNullException for a null one (measured " +
            "against .NET 10). The argument itself is not required to be a compile-time constant — a " +
            "runtime expression is accepted, matching .Key and .Ref — but a literal known at compile " +
            "time to always throw is rejected before it ships, the same way .Key(null) is specially " +
            "read rather than passed through unchanged.");

    public static readonly DiagnosticDescriptor BCF3040 = new(
        id: "BCF3040",
        title: "FormName is written on an element whose tag is not 'form'",
        messageFormat: "'.FormName' has no effect on a '{0}' element; onsubmit never fires natively " +
            "outside a 'form' element",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            ".FormName lowers to AddNamedEvent(\"onsubmit\", …), and onsubmit is a browser-native event " +
            "that only ever fires on a <form> element. An element helper's tag is already known as a " +
            "compile-time constant by this point (BCF3009), so this check needs no content-model table " +
            "— it is one string comparison, the same table-free posture BCF3034 takes. Falling short of " +
            "this check would leave the surface behind Razor, which warns (RZ10022) for the same shape.");

    /// <summary>
    /// BCF3041: A <c>.cs.css</c> file has no matching component or <c>[ViewPart]</c> declaration.
    /// </summary>
    /// <remarks>
    /// Stricter than Razor, which silently discards a convention-discovered orphan <c>.razor.css</c>
    /// and only errors (BLAZOR102) for an explicitly written <c>ScopedCssInput</c>. BCF has no
    /// explicit-input escape hatch — the <c>.cs.css</c>/<c>.cs</c> pairing is convention-only — so
    /// silently discarding it would let a typo in the file name go unnoticed. A
    /// file that declares only <c>[ViewPart]</c> methods (no component) is not orphaned: its scope
    /// still reaches rendered elements through expansion at every call site (ARCHITECTURE.md
    /// §2.7(F)), so the check counts a <c>[ViewPart]</c>-declaring file as a match too,
    /// not just a component-declaring one.
    /// </remarks>
    public static readonly DiagnosticDescriptor BCF3041 = new(
        id: "BCF3041",
        title: "Scoped CSS file has no matching component or view part",
        messageFormat: "'{0}' has no matching component or [ViewPart] declaration; " +
            "rename the file or add '{1}'",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A .cs.css file's scope is discovered by file-name convention (Foo.cs.css scopes Foo.cs), " +
            "with no explicit-pairing escape hatch. An orphan is therefore always a mistake — most " +
            "often a typo in the .cs.css file's name — rather than a deliberately unused file, so it " +
            "is reported rather than silently discarded (unlike Razor's own convention-discovered " +
            "orphan .razor.css, which Razor drops without comment).");

    /// <summary>
    /// BCF3042: <c>.Attr</c>/<c>.Class</c> on a <c>ComponentView&lt;TComponent&gt;</c> receiver whose
    /// name matches a declared <c>[Parameter]</c> case-insensitively. Blazor's own parameter binding
    /// matches names case-insensitively (measured), so left unguarded this would silently set the
    /// parameter and bypass <c>.Param</c>'s type checking entirely — the one guess this surface makes
    /// that DESIGN.md §4.1 requires to be verifiable, and here it is (<c>TComponent</c>'s declared
    /// members are known at the call site).
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3042 = new(
        id: "BCF3042",
        title: "Component attribute name collides with a declared parameter",
        messageFormat: "'{0}' matches the declared parameter '{1}' on '{2}' case-insensitively; bind " +
            "it through .Param(c => c.{1}, ...) (or .Template if '{1}' is a generic RenderFragment " +
            "slot) instead of .Attr/.Class, so the value is type-checked",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            ".Attr and .Class on a ComponentView<TComponent> exist to reach an HTML attribute the " +
            "target has no matching [Parameter] for, which Blazor routes into " +
            "[Parameter(CaptureUnmatchedValues = true)] at runtime. A name that does match a declared " +
            "[Parameter] is never that case: Blazor's own binding matches parameter names " +
            "case-insensitively, so an unguarded .Attr would silently set the parameter, bypassing " +
            ".Param's compile-time type check with nothing to show for it at the call site.");

    /// <summary>
    /// BCF3043: a <c>ForEach</c>'s source argument, or a native <c>foreach</c>'s source inside a
    /// <c>[ViewPart]</c> iterator's own body, is a call to a <c>[ViewPart]</c>. <c>ClassifyForEach</c>
    /// and <c>ClassifyIteratorForEach</c> normalized the source expression with
    /// <c>ExpressionTemplateFactory</c> alone and never asked <c>ClassifyCallee</c> about it, so the
    /// callee's design-time-built body ran at runtime uninspected, yielding one empty <c>View</c> per
    /// iteration (#578). DESIGN.md §4.3 names one supported call spelling for an iterator
    /// <c>[ViewPart]</c> — a spread in a child position — and a loop source is not it.
    /// </summary>
    public static readonly DiagnosticDescriptor BCF3043 = new(
        id: "BCF3043",
        title: "Loop source calls a [ViewPart]",
        messageFormat: "'{0}' is [ViewPart]; its result renders nothing when used as a loop source. " +
            "Spread it into a child position instead (e.g. 'Ul[.. {0}(...)]').",
        category: "BlazorCodeFirst",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "A [ViewPart]'s body is built from the design-time surface, which is inert: at runtime it " +
            "produces the default View regardless of what the call site passed. DESIGN.md §4.3 gives " +
            "an iterator [ViewPart] exactly one supported call spelling, a spread in a child position " +
            "(Ul[.. Rows(_items)]), which the generator expands statically. A loop source position " +
            "(ForEach's source argument, or a native foreach inside another [ViewPart]'s own iterator " +
            "body) is not that spelling: the call runs at runtime instead, against the inert surface, " +
            "so every yielded item is empty while the loop count stays correct. Rewrite the call as a " +
            "spread in a child position.");

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

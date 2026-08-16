---
title: Components and Reuse
order: 40
---

A component is the unit of reuse. One BlazorCodeFirst component calls another with
`Component<T>()`, an existing Razor or third-party component is called exactly the same way, and a
`.razor` file can call back into a BlazorCodeFirst component as an ordinary tag. `[ViewPart]` is
a different tool for a different job, covered at the end of this page.

## Calling another BlazorCodeFirst component

`Component<T>()` places a component into the tree. Parameters bind through `.Param`, naming the
target property with a lambda:

```csharp
protected override View Body =>
    Div.Class("dashboard")[
        Component<StatusBadge>()
            .Param(b => b.Status, _status)
            .Param(b => b.Compact, true)];
```

The generator turns each `.Param` into a static parameter setter, emitted as
`AddComponentParameter` calls. Nothing is reflected over and no expression tree is compiled at
runtime, which is what keeps the path trimming- and AOT-safe.

That is also why the shape is fenced in by diagnostics. Every channel that names a parameter with a
selector lambda answers to them — `.Param`, `.Template`, and the component `.Bind` alike, not
`.Param` alone:

- The selector must be a plain property selection ([BCF3005](./diagnostics.md#bcf3005)).
- The target must be a settable `[Parameter]` property ([BCF3006](./diagnostics.md#bcf3006)).
- Each property is bound at most once per chain, counting every channel
  ([BCF3007](./diagnostics.md#bcf3007)).

## Passing child content

Nested children bind to `ChildContent`, mirroring Razor's rule that nested content becomes
`ChildContent` and nothing else:

```csharp
protected override View Body =>
    Component<Card>()[
        H2["Heading"],
        P["Body text"]];
```

This requires `Card` to have a settable `[Parameter]` named `ChildContent` of a fragment type;
otherwise [BCF3013](./diagnostics.md#bcf3013) is reported. A `RenderFragment<TContext>` counts: the brackets bind it with the
context discarded, because there is no name inside brackets to read a context through. A generic
fragment under any *other* name is named with `.Template` instead — see
[Generic fragment parameters](#generic-fragment-parameters) below.

Other non-generic `RenderFragment` parameters (such as `Footer` or `Header`) bind through
`.Param(c => c.Footer, content)`, naming the parameter explicitly:

```csharp
protected override View Body =>
    Component<Card>()
        .Param(c => c.Title, "Card title")
        .Param(c => c.Footer, Span["Footer note"])[
            H2["Heading"],
            P["Body text"]];
```

Naming `ChildContent` through `.Param` is also legal. That is verbose, but it matches Razor's
attribute form (`<Card><ChildContent>...</ChildContent></Card>`). Binding the same parameter
through both channels reports [BCF3007](./diagnostics.md#bcf3007).

A real `RenderFragment` value (as opposed to a BlazorCodeFirst `View` expression) still binds
through the generic `.Param<TValue>` overload and is emitted verbatim.

Which overload runs is decided by the target parameter's type: a `RenderFragment?` parameter takes
the content overload, everything else takes the generic one. So content aimed at a parameter that is
not a `RenderFragment` lands on the generic overload, which emits its value verbatim — and the
runtime value of a design-time expression is an empty marker. That reports
[BCF3014](./diagnostics.md#bcf3014):

```csharp
[Parameter] public object? Payload { get; set; }

Component<Card>().Param(c => c.Payload, Div["x"])   // BCF3014
```

`View`, `ElementView`, `ComponentView<T>` and `SlotView` are all reported the same way. To pass
content, give the receiving component a `RenderFragment` parameter.

For unresolved type names inside parameter values, see
[BCF3015](./diagnostics.md#bcf3015).

## Generic fragment parameters

A `RenderFragment<TContext>` parameter takes a *template*: the component invokes it once per context
value it wants rendered. `EditForm.ChildContent` is the one most authors meet first — it is a
`RenderFragment<EditContext>`.

`.Template` names such a parameter. It has two spellings, and which one you want depends only on
whether the content reads the context. A generic fragment under a name other than `ChildContent`,
such as a grid's `RowTemplate`, always needs `.Template`, because the brackets never reach it.

For a `ChildContent` whose content ignores the context, the brackets are the spelling — the form
shown above, which emits exactly what `.Template(form => form.ChildContent, content)` emits:

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)[
            Component<NameFields>().Param(fields => fields.Value, _model)];
```

Or name it, with a lambda from the context to content:

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)
        .Template(form => form.ChildContent, editContext =>
            Fragment(
                Span[editContext.IsModified() ? "Unsaved changes" : "No changes"],
                Component<NameFields>().Param(fields => fields.Value, _model)));
```

That second example needs one caveat, or the badge will never change. `IsModified()` is read when the
template runs, and nothing in the `EditForm` / `CascadingValue` chain re-renders on `OnFieldChanged`.
Typing into a field does notify the `EditContext`, but with nothing subscribed to that notification
the component holding the template never re-renders, so the badge keeps the text it was first
rendered with. This is Blazor's render propagation, not a limit on
the context the template receives: the template is handed the live `EditContext` every time it runs.

So if a template reads context state that changes, the component owning the form has to re-render
itself. Construct the `EditContext` rather than letting `Model` create it, subscribe to
`OnFieldChanged`, call `StateHasChanged`, and unsubscribe in `Dispose`:

```csharp
public ContextReadingForm()
{
    _editContext = new EditContext(_model);
    _editContext.OnFieldChanged += OnFieldChanged;
}

private void OnFieldChanged(object? sender, FieldChangedEventArgs e) => StateHasChanged();

public void Dispose() => _editContext.OnFieldChanged -= OnFieldChanged;

// ...then pass .Param(form => form.EditContext, _editContext) instead of .Param(form => form.Model, …)
```

A template that ignores its context, or reads only state that does not change while the form is open,
needs none of this.

The generator writes the `RenderFragment<TContext>` lambda for you, so the content inside is ordinary
BlazorCodeFirst and its sequence numbers continue the surrounding ones. The context parameter's name
is yours to choose; the generated code uses a name of its own and rewrites the places you referred to
it, so a field that happens to share the name is not disturbed.

The second argument must be an inline lambda. A method group, or a delegate held in a variable or
field, reports [BCF3022](./diagnostics.md#bcf3022): what gets copied into the generated code is the lambda's body syntax, and
a delegate whose declaration is elsewhere has no body to copy.

If you already hold a `RenderFragment<TContext>` *value*, pass it through the scalar
`.Param` instead. Both channels reach the same parameter, but they differ in delegate identity, and
that difference is visible:

```csharp
// Built once, in the constructor. The parameter reference stays stable across renders.
private readonly RenderFragment<EditContext> _fields;
```

`.Template` content that reads state captures it, so the lambda becomes a new delegate on every
render. The receiving component sees a changed parameter and re-renders the template. A cached
delegate passed through `.Param` does not change, so it does not. Reach for the cached form only when
you want that stability; `.Template` is otherwise the shorter and safer spelling, because it cannot
be forgotten the way caching can.

## Calling an existing Razor or third-party component

The syntax does not change. A component written in `.razor`, or one from a package such as
MudBlazor or QuickGrid, is placed with the same `Component<T>()`:

```csharp
protected override View Body =>
    Div[
        Span["Data Grid"],
        Component<MudDataGrid<Order>>()
            .Param(g => g.Items, _orders)
            .Param(g => g.Dense, true)];
```

One restriction applies, and it is the first wall most authors hit. The type argument falls into
the generated code as a literal `OpenComponent<T>`, so it has to resolve while the generator runs.
The Razor compiler is itself a source generator, and source generators cannot observe each other's
output. A `.razor` component declared in the *same project* therefore does not exist yet when
BlazorCodeFirst's generator runs, and naming it reports [BCF3012](./diagnostics.md#bcf3012).

There are two ways around it:

- Move the `.razor` component into a referenced project or a package. Its type then comes from
  metadata and resolves normally.
- Hand-write the component in C#. A hand-written component is ordinary source, so it always
  resolves — including in the same project.

A typo or a missing `using` produces the same BCF3012, alongside CS0246 at the same position.

## Cascading a value

`CascadingValue<T>` is one of those existing components, so this surface adds nothing for cascading.
Place it with `Component<T>()`, set `Value` through `.Param`, and put the subtree that reads it in
brackets:

```csharp
protected override View Body =>
    Component<CascadingValue<ThemeInfo>>()
        .Param(c => c.Value, _theme)[
            Component<Toolbar>(),
            Component<Editor>()];
```

`Name` and `IsFixed` are `.Param` targets like any other, so a named cascade is
`.Param(c => c.Name, "locale")` on this side and `[CascadingParameter(Name = "locale")]` on the
receiving one.

That receiving side is ordinary Blazor, and the generator never sees it. `[CascadingParameter]` is a
property on the class, exactly as it is in a `.razor` component:

```csharp
public partial class Toolbar : BodyComponentBase
{
    [CascadingParameter]
    public ThemeInfo? Theme { get; set; }

    protected override View Body =>
        Div.Class("toolbar")[Span[Theme?.Name ?? "default"]];
}
```

Replacing the value re-renders every descendant that reads it, including ones whose own frames did
not change. The subscription is Blazor's own: what the generator emits for the call above is what
`<CascadingValue Value="@_theme">` emits.

## Using a BlazorCodeFirst component from Razor

The other direction has no such restriction. A BlazorCodeFirst component is a plain Blazor
component — `BodyComponentBase` derives from `ComponentBase` — so a `.razor` file names it as an
ordinary tag:

```razor
@* ExistingPage.razor *@
<div class="legacy-layout">
    <StatusBadge Status="@currentStatus" />
</div>
```

```csharp
public partial class StatusBadge : BodyComponentBase
{
    [Parameter] public Status Status { get; set; } = default!;

    protected override View Body =>
        Span.Class(Status.IsHealthy ? "badge badge-ok" : "badge badge-alert")[Status.Label];
}
```

This works in the same project, and it is worth understanding why the asymmetry with BCF3012
exists. What Razor has to resolve here is the *class name*, and that class declaration is source
you wrote. The generator only fills in `RenderView` inside it, and Razor never needs to see that.
In the BCF3012 direction, the type itself is generated output, which is a different problem.

This site does it: `App.razor` names `NotFoundPage`, a BlazorCodeFirst component declared in a
plain `.cs` file in the same project.

## Splitting without a component: `[ViewPart]`

Not every part of a `Body` expression deserves a component. A `[ViewPart]` method is a piece of
UI that the generator expands *into the caller* rather than rendering through a component
boundary:

```csharp
protected override View Body =>
    Div[
        AppHeader("My Application"),
        BodyContent()];

[ViewPart]
private static View AppHeader(string title) =>
    Div.Class("app-header")[
        Span[title]];
```

The caller's generated `RenderView` contains the header's frames directly. There is no component
instance, no parameters, no lifecycle, and no diffing boundary — it is as if you had written the
markup inline.

### Naming a component call

A part's body is ordinary design-time syntax, so it can be a component call as readily as an element.
That gives a component invocation a name of its own, and the call site stops mentioning
`Component<T>()` or `.Param` at all:

```csharp
public static class Widgets
{
    [ViewPart]
    public static View Badge(string label, bool compact = false) =>
        Component<StatusBadge>()
            .Param(b => b.Label, label)
            .Param(b => b.Compact, compact);
}

public partial class Dashboard : BodyComponentBase
{
    protected override View Body =>
        Div[
            Widgets.Badge("hello"),
            Widgets.Badge("x", compact: true)];
}
```

Named and optional arguments work, because the call site is an ordinary C# call. The expansion is
still an expansion: the caller's generated `RenderView` opens `StatusBadge` directly at each call, so
the rendered tree is the one writing `Component<StatusBadge>()` twice would have produced. The
`.Param` rules are checked where the selector is written, so BCF3006 and BCF3007 are reported once at
the part's declaration, however many call sites it has.

The component being named can come from anywhere, a package included, so `MudDataGrid<Order>` takes a
name on the same terms as a component you wrote. The part cannot: it is expanded from its
declaration's source syntax, so it has to live in the project that calls it (BCF1002, below). A
component library therefore ships components and not the parts that name them, and each consuming
project writes its own — the BCF3012 asymmetry seen from the other side.

### Wrapping content

A part that wraps content the caller supplies returns `SlotView` instead of `View`, and writes
`Slot` where that content belongs. The caller supplies it in brackets, exactly as it supplies an
element's children:

```csharp
protected override View Body =>
    Div[
        Card("Profile")[P["Body text"]],
        Section.Class("body")[P["…"]]];

[ViewPart]
private static SlotView Card(string title) =>
    Div.Class("card")[
        H2[title],
        Slot];
```

That is the point of the spelling: a part you factored out reads the same way a built-in element
reads. `Card("Profile")[…]` sits beside `Section.Class("body")[…]` without announcing that one of
them is yours.

The brackets are not optional, and nothing enforces that but C#. `SlotView` has no conversion to
`View`, so `Div[Card("Profile")]` — the brackets forgotten — is a compile error rather than a card
that renders silently empty. The same property rules out a decoration (`Card("t").Class("x")`, which
finds no extension method) and the positional spelling (`Card("t", P["x"])`, which has no parameter
to bind to).

A second slot is an ordinary `View` parameter:

```csharp
protected override View Body =>
    Panel(H2["Title"])[
        P["Body text"]];

[ViewPart]
private static SlotView Panel(View header) =>
    Div.Class("panel")[
        Div.Class("panel-head")[header],
        Div.Class("panel-body")[Slot]];
```

Named channels first, the main content in brackets — the shape `Div.Class("card")[…]` and
`Component<T>().Template(…)[…]` already have on this surface.

A slot sits in a component's brackets as readily as in an element's, which is how a named component
call takes content:

```csharp
[ViewPart]
private static SlotView Framed(string title) =>
    Component<Card>().Param(c => c.Title, title)[Slot];
```

The caller's content reaches `Card.ChildContent`, by the rule
[Passing child content](#passing-child-content) states.

Two rules are worth knowing. A `SlotView` part must name `Slot` **exactly once**: naming it twice
would emit the caller's content twice from one bracket, and never naming it would discard content the
caller was required to supply. Either reports [BCF3025](./diagnostics.md#bcf3025), as does a `Slot` written anywhere that
receives no caller content — a component's own `Body`, or a part returning `View`.

A `View` parameter, by contrast, may be referenced any number of times, because it is an ordinary
parameter. Nothing is captured or shared: each reference expands the caller's expression again, so an
argument with side effects runs once per reference. That is the same behaviour a Blazor
`RenderFragment` invoked twice has.

Both kinds are content, though, and content has no value: it becomes frames, not an expression. So a
slot can only be *placed* as a child. Reading one where a value is expected — as a `ForEach` key, or
inside an attribute value — reports [BCF1002](./diagnostics.md#bcf1002).

That is the whole trade-off, dimension by dimension:

| | `[ViewPart]` | A component |
| --- | --- | --- |
| State and lifecycle | none; it is a method | its own, as any Blazor component has |
| Re-rendering | with the caller, having no boundary of its own | on its own, at its own diffing boundary |
| What the caller's frames hold | the part's frames, expanded in place | one frame that opens the component |
| Arguments | by-value parameters, named and optional | `[Parameter]` properties, set through `.Param` |
| From another assembly | not available (BCF1002) | available |

A `[ViewPart]` has to satisfy a declaration contract the generator can expand, or it reports
**BCF1002**. The method must be:

- static
- non-generic, and declared in a non-generic type
- reaching one returned expression, with locals and expression statements allowed ahead of it — the
  same shape a `Body` getter takes
- returning `View`, or `SlotView` to take content

Its parameters must be ordinary by-value parameters whose types can be named from generated code.
`params`, by-reference parameters,
and `ElementView` parameters are all rejected — a childless element is passed as content by
writing `Div[…]` or `Fragment(Div)`, both of which are `View`s. A `View` parameter is a content slot,
so it requires the `SlotView` return type; on a part returning `View` it is BCF1002, and it may
never be optional.

It also must not be an extension member — neither a `this` parameter nor a member of an `extension`
block. A call is written as a plain call (`AppHeader("My Application")`), the way this surface writes
every call that is not a decoration on an element. The fluent spelling would put something that is
not a decoration in the position the surface reserves for one. Its receiver could only ever be some
other type's value, which would turn `[ViewPart]` into a way to grow *that* type's API rather than to
split up a `Body`.

BCF1002 also fires at the *call site*, and one of its conditions is worth stating plainly:

**a `[ViewPart]` cannot cross an assembly boundary.** Expanding a call needs the declaration's
source syntax, and the generator collects declarations from the compilation it is running in. IL
carries no body syntax, so a `[ViewPart]` in a referenced project or a package always reports
BCF1002 where it is called. The same diagnostic covers a recursive expansion cycle, and a body
that reaches a `private` or `protected` member the expansion site cannot see.

If you need the part in another project, make it a component and use it through `Component<T>()`.

BCF1002 is not only the `[ViewPart]` diagnostic. A component's own `Body` and a layout's `Chrome` are
normalized through the same check, and a report from there names the expression rather than a method
— see [BCF1002](./diagnostics.md#bcf1002).

## Next

See [layouts](./layouts.md) for wrapping routed pages in shared chrome,
[two-way binding](./two-way-binding.md#binding-a-component-parameter) for `.Bind`, the other way a
parameter is supplied, or [control flow](./control-flow.md) for `If` and keyed `ForEach`.

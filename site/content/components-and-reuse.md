---
title: Components and Reuse
order: 40
---

A component is the unit of reuse. One BlazorCodeFirst component calls another with
`Component<T>()`, an existing Razor or third-party component is called exactly the same way, and a
`.razor` file can call back into a BlazorCodeFirst component as an ordinary tag. `[Composable]` is
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

That is also why the shape is fenced in by diagnostics:

- The selector must be a plain property selection. A cast, a method call, or a member of a
  captured variable reports BCF3005, because none of those name a property the generator can emit
  a setter for.
- The target must be a settable `[Parameter]` property, or BCF3006 is reported. Blazor would
  otherwise throw at runtime, so the rejection is moved to compile time.
- Binding the same property twice reports BCF3007. Blazor applies only the last value, so the
  earlier binding would silently die.

## Passing child content

Nested children bind to `ChildContent`, mirroring Razor's rule that nested content becomes
`ChildContent` and nothing else:

```csharp
protected override View Body =>
    Component<Card>()[
        H2["Heading"],
        P["Body text"]];
```

This requires `Card` to have a settable `[Parameter] public RenderFragment? ChildContent`;
otherwise BCF3013 is reported. A `RenderFragment<TContext>` parameter cannot receive the children,
because the generated lambda is non-generic and would fail an invalid cast at runtime.

Other `RenderFragment` parameters (such as `Footer` or `Header`) bind through
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
through both channels reports BCF3007.

A real `RenderFragment` value (as opposed to a BlazorCodeFirst `View` expression) still binds
through the generic `.Param<TValue>` overload and is emitted verbatim.

For unresolved type names inside parameter values, see
[Values copied into generated code](./getting-started.md#values-copied-into-generated-code).

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
BlazorCodeFirst's generator runs, and naming it reports **BCF3012**.

There are two ways around it:

- Move the `.razor` component into a referenced project or a package. Its type then comes from
  metadata and resolves normally.
- Hand-write the component in C#. A hand-written component is ordinary source, so it always
  resolves — including in the same project.

A typo or a missing `using` produces the same BCF3012, alongside CS0246 at the same position.

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

## Splitting without a component: `[Composable]`

Not every part of a `Body` expression deserves a component. A `[Composable]` method is a piece of
UI that the generator expands *into the caller* rather than rendering through a component
boundary:

```csharp
protected override View Body =>
    Div[
        AppHeader("My Application"),
        BodyContent()];

[Composable]
private static View AppHeader(string title) =>
    Div.Class("app-header")[
        Span[title]];
```

The caller's generated `RenderView` contains the header's frames directly. There is no component
instance, no parameters, no lifecycle, and no diffing boundary — it is as if you had written the
markup inline.

That is the whole trade-off:

- **Reach for `[Composable]`** when the part is pure projection: it has no state of its own, and
  you want it inlined rather than sitting behind a boundary.
- **Reach for a component** when the part holds state, needs a lifecycle, should re-render on its
  own, or is used from another assembly.

A `[Composable]` has to satisfy a declaration contract the generator can expand, or it reports
**BCF1002**. It must be a static, non-generic, expression-bodied method returning `View`, declared
in a non-generic type, and its parameters must be ordinary by-value parameters whose types can be
named from generated code. `View` and `ElementBuilder` parameters, `params`, and by-reference
parameters are all rejected.

BCF1002 also fires at the *call site*, and one of its conditions is worth stating plainly:

**a `[Composable]` cannot cross an assembly boundary.** Expanding a call needs the declaration's
source syntax, and the generator collects declarations from the compilation it is running in. IL
carries no body syntax, so a `[Composable]` in a referenced project or a package always reports
BCF1002 where it is called. The same diagnostic covers a recursive expansion cycle, and a body
that reaches a `private` or `protected` member the expansion site cannot see.

If you need the part in another project, make it a component and use it through `Component<T>()`.

## Next

See [layouts](./layouts.md) for wrapping routed pages in shared chrome,
[two-way binding](./two-way-binding.md#binding-a-component-parameter) for `.Bind`, the other way a
parameter is supplied, or [control flow](./control-flow.md) for `If` and keyed `ForEach`.

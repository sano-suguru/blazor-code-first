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

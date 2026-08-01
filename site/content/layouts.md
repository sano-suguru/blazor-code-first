---
title: Layouts
order: 40
---

A layout wraps the routed page with shared chrome — headers, navigation, footers. BlazorCompose
layouts are written the same way as components: derive from `ComposeLayoutBase`, declare a
design-time UI expression, and let the source generator produce the rendering.

## Chrome and Body

`ComposeLayoutBase` derives from Blazor's `LayoutComponentBase`, so it already has a `Body`
parameter holding the routed page. The chrome the layout itself draws goes in a separate
overridden property, `Chrome`:

```csharp
using BlazorCompose;
using static BlazorCompose.Html;

public partial class MainLayout : ComposeLayoutBase
{
    protected override View Chrome =>
        Div.Class("shell")[
            Header[H1["My App"]],
            Main.Class("content")[Body],
            Footer["© 2026"]];
}
```

`Main[Body]` here is exactly `<main>@Body</main>` in Razor — the routed page dropped in as
element content.

## Why Chrome, not Body

Blazor requires a layout's routed content to be exposed through a parameter named exactly
`Body`, and C# cannot declare two members with the same name on one type. So `Body` keeps
its Razor meaning — the page being wrapped — and the layout's own design-time expression is
named `Chrome` instead.

## Nesting layouts

A layout can itself sit inside another layout. Put `[Layout]` on the layout type, exactly as you
would on a page:

```csharp
using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

[Layout(typeof(SiteLayout))]
public partial class DocsLayout : ComposeLayoutBase
{
    protected override View Chrome => Div.Class("docs")[Aside[TableOfContents()], Main[Body]];
}
```

Nesting is resolved by Blazor, not by BlazorCompose: `LayoutView` reads the attribute off the layout
type and wraps it in its own layout, and a Compose layout is an ordinary `LayoutComponentBase`
descendant. Each level's `Body` holds the level below it — `SiteLayout`'s `Body` is the rendered
`DocsLayout`, whose own `Body` is the routed page.

## RenderFragment becomes content directly

`Body` is a plain Blazor `RenderFragment?`, not a BlazorCompose type, yet `Main[Body]` above
compiles without a dedicated factory: `View` has an implicit conversion from `RenderFragment?`,
so any fragment can appear wherever element content is expected. The conversion is from the
non-generic `RenderFragment` only — a `RenderFragment<T>` does not convert. And like `Fragment`
and `Raw`, a `RenderFragment` opens no keyable frame, so it cannot be a `ForEach` content root and
cannot carry decorations.

The same mechanism lets a Compose component render children passed in from Razor. A component
with `[Parameter] public RenderFragment? ChildContent` uses it exactly like `Body`:

```csharp
using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

public partial class Card : ComposeComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override View Body => Div.Class("card")[ChildContent];
}
```

## Passing child content to components

The direction above — Razor passing content into a Compose component — uses the implicit
`RenderFragment?` conversion. The opposite direction — Compose code passing content to a Razor or
hand-written Blazor component — uses `Component<T>()` with nested children or the `.Param` overload
for `RenderFragment` parameters.

Nested children bind to `ChildContent`, mirroring Razor's rule that nested content becomes
`ChildContent` and nothing else:

```csharp
protected override View Body =>
    Component<Card>()[
        H2["Heading"],
        P["Body text"]];
```

This requires `Card` to have a settable `[Parameter] public RenderFragment? ChildContent`; otherwise
BC3013 is reported. A `RenderFragment<TContext>` parameter cannot receive the children — the
generated lambda is non-generic and would fail an invalid cast at runtime.

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

It is also legal to name `ChildContent` through `.Param` — this is verbose but matches Razor's
attribute form (`<Card><ChildContent>...</ChildContent></Card>`). Binding the same parameter through
both channels reports BC3007.

A real `RenderFragment` value (as opposed to a BlazorCompose `View` expression) still binds through
the generic `.Param<TValue>` overload and is emitted verbatim.

For unresolved type names inside parameter values, see
[Values copied into generated code](./getting-started.md#values-copied-into-generated-code).

## Reads are allowed, mutation is not

Both `Chrome` and `Body` (the `ComposeComponentBase` one, not the layout's routed-content
parameter) may read component state — projecting state to UI is their whole purpose — but
neither may mutate it. Mutating state inside either reports BC3001, the same diagnostic that
applies to a regular component's `Body`.

## Next

See [elements and decorations](./elements-and-decorations.md) for the element vocabulary used
above, or [control flow](./control-flow.md) for `If` and keyed `ForEach`.

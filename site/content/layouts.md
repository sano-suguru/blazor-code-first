---
title: Layouts
order: 40
---

A layout wraps the routed page with shared chrome — headers, navigation, footers. BlazorCodeFirst
layouts are written the same way as components: derive from `ChromeLayoutBase`, declare a
design-time UI expression, and let the source generator produce the rendering.

## Chrome and Body

`ChromeLayoutBase` derives from Blazor's `LayoutComponentBase`, so it already has a `Body`
parameter holding the routed page. The chrome the layout itself draws goes in a separate
overridden property, `Chrome`:

```csharp
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

public partial class MainLayout : ChromeLayoutBase
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
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

[Layout(typeof(SiteLayout))]
public partial class DocsLayout : ChromeLayoutBase
{
    protected override View Chrome => Div.Class("docs")[Aside[TableOfContents()], Main[Body]];
}
```

Nesting is resolved by Blazor, not by BlazorCodeFirst: `LayoutView` reads the attribute off the layout
type and wraps it in its own layout, and a BlazorCodeFirst layout is an ordinary `LayoutComponentBase`
descendant. Each level's `Body` holds the level below it — `SiteLayout`'s `Body` is the rendered
`DocsLayout`, whose own `Body` is the routed page.

## RenderFragment becomes content directly

`Body` is a plain Blazor `RenderFragment?`, not a BlazorCodeFirst type, yet `Main[Body]` above
compiles without dedicated syntax: `View` has an implicit conversion from `RenderFragment?`,
so any fragment can appear wherever element content is expected. The conversion is from the
non-generic `RenderFragment` only — a `RenderFragment<T>` does not convert. And like `Fragment`
and `Raw`, a `RenderFragment` opens no keyable frame, so it cannot be a `ForEach` content root and
cannot carry decorations.

The same mechanism lets a BlazorCodeFirst component render children passed in from Razor. A component
with `[Parameter] public RenderFragment? ChildContent` uses it exactly like `Body`:

```csharp
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

public partial class Card : BodyComponentBase
{
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override View Body => Div.Class("card")[ChildContent];
}
```

## Passing child content to components

The direction above — Razor passing content into a BlazorCodeFirst component — uses the implicit
`RenderFragment?` conversion. The opposite direction — BlazorCodeFirst code passing content to a Razor or
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
BCF3013 is reported. A `RenderFragment<TContext>` parameter cannot receive the children — the
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
both channels reports BCF3007.

A real `RenderFragment` value (as opposed to a BlazorCodeFirst `View` expression) still binds through
the generic `.Param<TValue>` overload and is emitted verbatim.

For unresolved type names inside parameter values, see
[Values copied into generated code](./getting-started.md#values-copied-into-generated-code).

## Reads are allowed, mutation is not

Both `Chrome` and `Body` (the `BodyComponentBase` one, not the layout's routed-content
parameter) may read component state — projecting state to UI is their whole purpose — but
neither may mutate it. Mutating state inside either reports BCF3001, the same diagnostic that
applies to a regular component's `Body`.

## Next

See [elements and decorations](./elements-and-decorations.md) for the element vocabulary used
above, or [control flow](./control-flow.md) for `If` and keyed `ForEach`.

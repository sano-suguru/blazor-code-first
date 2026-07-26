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
        Div(
            Header(H1("My App")),
            Main(Body).Class("content"),
            Footer("© 2026"))
        .Class("shell");
}
```

`Main(Body)` here is exactly `<main>@Body</main>` in Razor — the routed page dropped in as
element content.

## Why Chrome, not Body

Blazor requires a layout's routed content to be exposed through a parameter named exactly
`Body`, and C# cannot declare two members with the same name on one type. So `Body` keeps
its Razor meaning — the page being wrapped — and the layout's own design-time expression is
named `Chrome` instead.

## RenderFragment becomes content directly

`Body` is a plain Blazor `RenderFragment?`, not a BlazorCompose type, yet `Main(Body)` above
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

    protected override View Body => Div(ChildContent).Class("card");
}
```

## Reads are allowed, mutation is not

Both `Chrome` and `Body` (the `ComposeComponentBase` one, not the layout's routed-content
parameter) may read component state — projecting state to UI is their whole purpose — but
neither may mutate it. Mutating state inside either reports BC3001, the same diagnostic that
applies to a regular component's `Body`.

## Next

See [elements and decorations](./elements-and-decorations.md) for the element vocabulary used
above, or [control flow](./control-flow.md) for `If` and keyed `ForEach`.

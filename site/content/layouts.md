---
title: Layouts
order: 50
---

A layout wraps the routed page with shared chrome: headers, navigation, footers. BlazorCodeFirst
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

`Main[Body]` here is exactly `<main>@Body</main>` in Razor, the routed page dropped in as element
content. The output below stands a placeholder where the page would land:

<!-- bcf-figure: AppShell -->

```csharp
protected override View Chrome =>
    Div.Class("shell")[
        Header[H1["My App"]],
        Main.Class("content")[Body],
        Footer["© 2026"]];
```

```html
<div class="shell">
    <header><h1>My App</h1></header>
    <main class="content">the routed page</main>
    <footer>© 2026</footer>
</div>
```

## Why Chrome, not Body

Blazor requires a layout's routed content to be exposed through a parameter named exactly
`Body`, and C# cannot declare two members with the same name on one type. So `Body` keeps
its Razor meaning, the page being wrapped, and the layout's own design-time expression is
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
descendant. Each level's `Body` holds the level below it. `SiteLayout`'s `Body` is the rendered
`DocsLayout`, whose own `Body` is the routed page.

## RenderFragment becomes content directly

`Body` is a plain Blazor `RenderFragment?`, not a BlazorCodeFirst type, yet `Main[Body]` above
compiles without dedicated syntax. `View` has an implicit conversion from `RenderFragment?`,
so any fragment can appear wherever element content is expected. The conversion is from the
non-generic `RenderFragment` only, and a `RenderFragment<T>` does not convert. Like `Fragment`
and `Raw`, a `RenderFragment` opens no keyable frame, so it cannot be a `ForEach` content root
([BCF3003](./diagnostics.md#bcf3003)) and cannot carry decorations
([BCF3008](./diagnostics.md#bcf3008)).

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

Passing content the other way, from BlazorCodeFirst code into a Razor or hand-written component,
uses `Component<T>()`. See
[passing child content](./components-and-reuse.md#passing-child-content).

## Reads are allowed, mutation is not

Both `Chrome` and `Body` may read component state, since projecting state to UI is their whole
purpose, but neither may mutate it. `Body` here means the `BodyComponentBase` one, not the layout's
routed-content parameter. Mutating state inside either reports
[BCF3001](./diagnostics.md#bcf3001), the same diagnostic that applies to a regular component's
`Body`.

## Next

See [components and reuse](./components-and-reuse.md) for calling one component from another, or
[elements and decorations](./elements-and-decorations.md) for the element vocabulary used above.

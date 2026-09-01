---
title: From Razor
description: The translation table from Razor syntax to this surface, and the four places where the shape of a component differs rather than its spelling.
order: 30
group: start
---

This page is the translation table from Razor syntax to this surface, plus the four places where the
shape of a component differs rather than its spelling.

## What is the same

A component here is a Blazor component. `BodyComponentBase` derives from `ComponentBase`, so
lifecycle methods, `[Parameter]`, `[Inject]`, `[CascadingParameter]`, `StateHasChanged` and
`IDisposable` are the ones you already use. A `.razor` file can call one of these components, and
one of these can call a `.razor` component
([components and reuse](./components-and-reuse.md#calling-an-existing-razor-or-third-party-component)).

## The translation table

| Razor | Here |
| --- | --- |
| `@page "/counter"` | `[Route("/counter")]` |
| `@inject IJSRuntime Js` | `[Inject] private IJSRuntime Js { get; set; }` |
| `@code { … }` | ordinary class members |
| `<div class="card">…</div>` | `Div.Class("card")[…]` |
| `<img src="/x.png" />` | `Img.Src("/x.png")` |
| `@onclick="Save"` | `.OnClick(() => Save())` |
| `@bind="_name"` | `.Bind("value", "oninput", () => _name)` |
| `@if (x) { … } else { … }` | `If(x, () => …, () => …)` |
| `@foreach (var r in rows) { … }` | `ForEach(rows, key: r => r.Id, content: r => …)`, or splice a `[ViewPart]` iterator ([control flow](./control-flow.md#iterating-with-a-viewpart)) |
| `@key="r.Id"` | the `key` argument of `ForEach`, or `.Key(…)` |
| `@ref="_el"` | `.Ref(…)` |
| `<Card Title="x" />` | `Component<Card>().Param(c => c.Title, "x")` |
| `<Card>…</Card>` | `Component<Card>()[…]` |
| `<CascadingValue Value="@_t">` | `Component<CascadingValue<T>>().Param(c => c.Value, _t)[…]` |
| `@((MarkupString)html)` | `Raw(html)` |
| `<text>…</text>` | `Fragment(…)` |
| `@layout MainLayout` | `[Layout(typeof(MainLayout))]`, unchanged |
| `@Body` in a layout | `Body`, inside an overridden `Chrome` ([layouts](./layouts.md)) |

## Four differences beyond spelling

### Attributes come before children

Razor lets you write an attribute anywhere in the start tag and children after it. Here the
decorations chain onto the element and the children go in brackets, in that order. The brackets
already produced a `View`, so nothing may decorate an element after them
([BCF3008](./diagnostics.md#bcf3008)).

### The getter reaches one expression

A `.razor` file is a template with statements in it. A `Body` is one returned expression, with
locals allowed ahead of the return. A second return needs a sequence space of its own, which is why
it is not accepted; a native `if`/`switch` may end the getter instead, though it degrades
([BCF1004](./diagnostics.md#bcf1004), [BCF2002](./diagnostics.md#bcf2002)).

That is also why `If` and `ForEach` exist as constructs rather than as C# keywords. Read
[control flow](./control-flow.md) before rewriting a template with branches in it.

### A key is required, or explicitly declined

`@key` is optional in Razor and easy to leave off. `ForEach` has no default for `key`, so a list
either identifies its items or says `key: null` and accepts the cost. That is
[declining the key](./control-flow.md#declining-the-key).

### There is no second vocabulary

Every element is the HTML element of the same name, and CSS does the layout. There is no `VStack`,
no `.Padding()`, and no `Text()` — a bare string is a text node. What you know about HTML is what
there is to know about the element surface.

## Rewriting one component

Start with the markup and leave the C# alone. Move `@code` members to the class body, replace the
template with a `Body` getter, and let the build tell you what it cannot translate. Every diagnostic
names what to write instead, and the [reference](./diagnostics.md) has an entry for each.

The mistakes that come first are mechanical: a class that is not `partial`
([BCF1001](./diagnostics.md#bcf1001)), and a getter that still has statements after its return
([BCF1004](./diagnostics.md#bcf1004)).

## Next

- [Elements and decorations](./elements-and-decorations.md) for the element surface in full.
- [Components and reuse](./components-and-reuse.md#using-a-blazorcodefirst-component-from-razor) for
  mixing the two kinds of component in one project.

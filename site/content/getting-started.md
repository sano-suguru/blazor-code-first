---
title: Getting Started
description: Install the runtime and the source generator, derive a component from BodyComponentBase, and render your first Blazor UI written as plain C#.
order: 10
group: start
---

BlazorCodeFirst lets you write Blazor UI as plain C#.

## Installation

Add the runtime and the source generator to your project, then derive your components from
`BodyComponentBase`.

```
dotnet add package BlazorCodeFirst --prerelease
```

The published version carries a prerelease suffix. Without `--prerelease` the command looks for the
latest stable version, of which there is none, and resolves nothing.

## A first component

A component is a `partial` class with one overridden property. The class must be `partial`, because
the generator writes the rendering into it, and top-level, because reopening a nested class from the
generated file would mean re-declaring every enclosing type, type parameters included.

```csharp
using Microsoft.AspNetCore.Components;
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

[Route("/")]
public partial class Home : BodyComponentBase
{
    protected override View Body =>
        Div[
            H1["Hello"],
            Span["Welcome to BlazorCodeFirst."]];
}
```

That expression names the HTML it produces. Attributes chain onto the element, children go in
brackets, and a bare string is a text node.

<!-- bcf-figure: Greeting -->

```csharp
protected override View Body =>
    Div[
        H1["Hello"],
        Span["Welcome to BlazorCodeFirst."]];
```

```html
<div>
    <h1>Hello</h1>
    <span>Welcome to BlazorCodeFirst.</span>
</div>
```

## The getter reaches one expression

Three spellings satisfy that, and all three translate identically:

```csharp
protected override View Body => Div[H1["Hello"]];                    // fine
protected override View Body { get => Div[H1["Hello"]]; }            // fine
protected override View Body { get { return Div[H1["Hello"]]; } }    // fine
```

Locals and expression statements may precede that return. They are copied into the generated
`RenderView` ahead of the calls that emit render-tree frames, which is where a `ForEach` content
block's statements already go:

```csharp
protected override View Body
{
    get
    {
        var greeting = $"Hello, {_name}";
        return Div[H1[greeting]];
    }
}
```

A second return and native control flow each need a sequence space of their own, so neither is
accepted ([BCF1004](./diagnostics.md#bcf1004)). If the body genuinely cannot be written in this
shape, override `RenderView` by hand: the design-time expression is then unused, and nothing is
reported.

## Where the surface is read

`Html.Div`, `.Class(...)`, `.OnClick(...)` and every other factory and decoration are inert. `View`
is an empty struct, an element helper returns nothing, and a decoration returns its receiver
unchanged. The generator reads the *syntax* you wrote, never the value, and it reads it in three
places: a component's `Body`, a layout's `Chrome`, and the body of a `[ViewPart]` method.

The same API is callable from anywhere, and nothing reads it outside those three places. Written in
an event handler, a service, or a helper method it still compiles, but it emits no render-tree
frames, so nothing is rendered and no event handler is registered. That is
[BCF3029](./diagnostics.md#bcf3029), and
[BCF3030](./diagnostics.md#bcf3030) is the same mistake seen from a call site.

## Why the build stops

The compiler reports any expression it cannot translate into `RenderTreeBuilder` calls, rather than
emitting something that renders differently from what you wrote. Every diagnostic has an entry in
the [reference](./diagnostics.md), and these five are the ones reported most often:

| | |
| --- | --- |
| [BCF1001](./diagnostics.md#bcf1001) | the class is not `partial` |
| [BCF1005](./diagnostics.md#bcf1005) | the class is nested |
| [BCF1004](./diagnostics.md#bcf1004) | the getter does not reach one returned expression |
| [BCF1002](./diagnostics.md#bcf1002) | the expression names a local the generated file cannot see |
| [BCF1003](./diagnostics.md#bcf1003) | the expression uses a construct the generator does not read |

These five reject a shape the generator cannot read at all. The same rule also catches shapes that
read fine but would render two different DOMs: [BCF3016](./diagnostics.md#bcf3016) rejects children
on a void element, because prerendering closes the tag and the HTML parser pushes them out to
siblings, while interactive rendering never opens that path.

A class can carry two faults at once, a missing `partial` and an untranslatable getter, and only one
is reported at a time. The `partial` check runs first, so BCF1001 is reported alone; adding the
modifier is what surfaces BCF1004.

## Next steps

- Read the [counter sample](/counter) to see events, `If`, and keyed `ForEach`.
- Learn the [element vocabulary](./elements-and-decorations.md) and
  [control flow](./control-flow.md#if).

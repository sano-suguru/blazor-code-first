---
title: Getting Started
order: 10
group: start
---

BlazorCodeFirst lets you write Blazor UI as plain C#. This page is itself rendered from Markdown,
converted at build time and injected through `Html.Raw`.

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
the generator writes the rendering into it, and top-level, because generated code cannot reproduce a
chain of enclosing types.

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

Locals and expression statements may precede that return. They are transplanted into the generated
rendering ahead of the frames, which is where a `ForEach` content block's statements already go:

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

## Where the surface means something

`Html.Div`, `.Class(...)`, `.OnClick(...)` and every other factory and decoration are inert. `View`
is an empty struct, an element helper returns nothing, and a decoration returns its receiver
unchanged. The generator reads the *syntax* you wrote, never the value, and it reads it in three
places: a component's `Body`, a layout's `Chrome`, and the body of a `[ViewPart]` method.

The same API is callable from anywhere, and elsewhere it means nothing. Written in an event handler,
a service, or a helper method it still compiles, still looks like it built something, and does
nothing at all — no output, and no handler wired up. That is
[BCF3029](./diagnostics.md#bcf3029), and [BCF3030](./diagnostics.md#bcf3030) is the same mistake
seen from a call site.

## When the build stops

The compiler reports what it cannot translate rather than emitting something that renders
differently from what you wrote. Every diagnostic has an entry in the
[reference](./diagnostics.md), and these are the ones you meet first:

| | |
| --- | --- |
| [BCF1001](./diagnostics.md#bcf1001) | the class is not `partial` |
| [BCF1005](./diagnostics.md#bcf1005) | the class is nested |
| [BCF1004](./diagnostics.md#bcf1004) | the getter does not reach one returned expression |
| [BCF1002](./diagnostics.md#bcf1002) | the expression names a local the generated file cannot see |
| [BCF1003](./diagnostics.md#bcf1003) | the expression uses a construct the generator does not read |

A class can carry two faults at once — a missing `partial` and an untranslatable getter — and you
are told about one at a time. The `partial` check runs first, so BCF1001 comes alone, and adding the
modifier is what surfaces BCF1004.

## Next steps

- Read the [counter sample](/counter) to see events, `If`, and keyed `ForEach`.
- Learn the [element vocabulary](./elements-and-decorations.md) and
  [control flow](./control-flow.md#if).

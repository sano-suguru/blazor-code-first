---
title: Getting Started
order: 10
---

BlazorCodeFirst lets you write Blazor UI as plain C#. This page is itself rendered
from Markdown, converted at build time and injected through `Html.Raw`.

## Installation

Add the runtime and the source generator to your project, then derive your
components from `BodyComponentBase`.

## A first component

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

## Where the surface means something

`Html.Div`, `.Class(...)`, `.OnClick(...)` and every other factory and decoration are inert. `View` is
an empty struct, an element helper returns nothing, and a decoration returns its receiver unchanged.
The generator reads the *syntax* you wrote, never the value, and it reads it in three places: a
component's `Body`, a layout's `Chrome`, and the body of a `[ViewPart]` method.

The same API is callable from anywhere, and elsewhere it means nothing. Written in an event handler, a
service, or a helper method it still compiles, still looks like it built something, and does nothing
at all — no output, and no handler wired up:

```csharp
private void OnSomething()
{
    // BCF3029: renders nothing, and DoThing is never called
    var card = Div.Class("card").OnClick(DoThing)[Span["hello"]];
}
```

BCF3029 names that. All three reading positions are recognized by returning one of the design-time
types, so nothing about them is a special case: a lambda that returns a `View`, such as the content of
an `If` or a `ForEach`, is read the same way. Caching a value into a field or property of a
design-time type is left alone; only a local, a discard, or an argument is reported.

## Values copied into generated code

BlazorCodeFirst copies design-time value expressions into a generated file that has no `using`
directives. Resolved type names are rewritten as `global::`-qualified names. If a type is still
unresolved and its spelling depends on the source file's lexical context, the generator reports
BCF3015 at that type name.

Fix the name, fully qualify it, move a source-generated type to a referenced project, or replace it
with a hand-written C# type. A reference already rooted at `global::` is preserved and left to normal
C# resolution. Generic type arguments are checked independently.

## Next steps

- Read the [counter sample](/counter) to see events, `If`, and keyed `ForEach`.
- Jump to [Installation](#installation) or [A first component](#a-first-component).
- Learn the [element vocabulary](./elements-and-decorations.md) and [control flow](./control-flow.md#if).

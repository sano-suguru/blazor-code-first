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

## What the class has to be

The generator writes a `RenderView` override into the class you declared, so that class has to be one
it can write into. Three shapes are rejected, each by its own diagnostic.

The class must be `partial`, or BCF1001 is reported. Only the class that declares the override needs
the modifier, because nothing is generated into any of these:

- an intermediate abstract base
- a leaf whose base already declares the override
- a re-abstraction

The class must be top-level, or BCF1005 is reported. Generated code cannot reproduce a chain of
enclosing type declarations, including any enclosing type's type parameters, so a nested component is
rejected rather than half emitted. Without the diagnostic the nesting would surface only as CS0534
against the abstract `RenderView`. CS0534 names the missing member, never the type it is nested in.

The getter must reduce to a single expression, or BCF1004 is reported. Three spellings satisfy that,
and all three translate identically:

```csharp
protected override View Body => Div[H1["Hello"]];              // fine
protected override View Body { get => Div[H1["Hello"]]; }      // fine
protected override View Body { get { return Div[H1["Hello"]]; } }   // fine

protected override View Body                                   // BCF1004: a local before the return
{
    get
    {
        var greeting = "Hello";
        return Div[H1[greeting]];
    }
}

protected override View Body { get; } = default;               // BCF1004: an auto property has no getter body
```

A getter holding statements has no single expression to translate, and an auto property declares no
getter body at all. BCF1004 blames the declaration, and that is what separates it from BCF1003.
BCF1003 means the getter's shape was fine and something written inside it could not be sequenced. If
the body genuinely cannot be one expression, override `RenderView` by hand — the design-time
expression is then unused, and nothing is reported.

A class can carry two of these faults at once — a missing `partial` and an untranslatable getter — and
you are told about one at a time. The `partial` check runs first, so BCF1001 comes alone, and adding
the modifier is what surfaces BCF1004.

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

BCF3029 names that. What marks a reading position is the design-time type it returns, so none of the
three is a special case. A lambda that returns a `View` — the content of an `If` or a `ForEach` — is
read the same way. Caching a value into a field or property of a design-time type is left alone; only
a local, a discard, or an argument is reported.

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

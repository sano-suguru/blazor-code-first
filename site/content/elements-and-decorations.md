---
title: Elements and Decorations
order: 20
---

BlazorCodeFirst mirrors HTML directly: every element you write in a `Body` expression names the
element it produces. There is no intermediate widget vocabulary to learn and no runtime UI tree.
The source generator turns these calls into `RenderTreeBuilder` instructions at compile time.

## Elements

Element helpers take mixed string and element children in brackets:

```csharp
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

protected override View Body =>
    Article[
        H2["Elements"],
        P["Text and ", A.Href("/docs")["links"], " compose in one call."],
        Ul[
            Li["Structure: Div, Section, Article, Header, Footer, Main, Aside, Nav"],
            Li["Text: Span, P, H1 through H6"],
            Li["Lists: Ul, Ol, Li"],
            Li["Interactive: Button"],
            Li["Links and media: A, Img"]]];
```

For an element without a dedicated helper, use `Element` with a compile-time constant tag:

```csharp
Element("figure")[Img.Src("/diagram.png").Alt("Architecture")]
```

## Children

A bare string converts to a text node, so there is no separate `Text()` construct. An element with
no children drops the brackets entirely, and a Blazor `RenderFragment` sits in the child list like
any other child:

```csharp
[Parameter] public RenderFragment? ChildContent { get; set; }

protected override View Body =>
    Div[
        "plain text",
        Img.Src("/logo.png").Alt("Logo"),
        ChildContent];
```

The generator has to see each child as its own expression to give it a sequence number. Writing the
children as a nested collection literal is accepted, because C# expands it to the same call:

```csharp
Div[["a", "b"]]     // same as Div["a", "b"], which is the form to prefer
```

Handing over a child list the generator cannot see through reports BCF1003. That covers a variable
or method result passed whole (`Div[_kids]`), an explicit array (`Div[new View[] { … }]`), and any
spread (`Div[[..items]]`). Use [`ForEach`](./control-flow.md#keyed-foreach) for repetition instead.

## Decorations

Decorations are chained onto the element they belong to, before its children, the way HTML writes
attributes inside the tag. They collapse into the owning element's attributes rather than
introducing wrapper nodes:

```csharp
Button
    .Class("btn btn-primary")
    .Title("Save the current document")
    .OnClick(() => Save())["Save"];
```

Available decorations are `.Class`, `.Id`, `.Href`, `.Src`, `.Alt`, `.Type`, `.Title`, `.Role`,
`.OnClick`, and the general-purpose escape hatches `.Attr(name, value)` and `.On(eventName, handler)`.

`.On` takes the full attribute name including the `on` prefix (`.On("onmouseenter", …)`); nothing is
prefixed for you. The name given to `.Attr` or `.On` must be a non-empty compile-time constant, or
the generator reports BCF3011.

`class` is the one attribute that folds: chaining `.Class` more than once merges the values into a
single `class` attribute. Every other attribute and event is a single binding, and binding one twice
on the same element reports BCF3010. There is no `style` shortcut, so prefer an external stylesheet
and `.Class`.

A decoration must target a single element. Applying one to `If`, `ForEach`, `Fragment`, `Raw`, or a
component result reports diagnostic BCF3008, because those constructs open no element to attach to.
Writing the chain after the children (`Div["text"].Class("card")`) reports BCF3008 for the same
reason: the brackets have already produced a `View`.

## Next

Read [control flow](./control-flow.md) for conditionals and lists, or go back to
[getting started](./getting-started.md#installation).

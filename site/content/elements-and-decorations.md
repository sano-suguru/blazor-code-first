---
title: Elements and Decorations
order: 20
---

BlazorCompose mirrors HTML directly: every element you write in a `Body` expression names the
element it produces. There is no intermediate widget vocabulary to learn and no runtime UI tree —
the source generator turns these calls into `RenderTreeBuilder` instructions at compile time.

## Elements

Element factories take mixed string and element children:

```csharp
using BlazorCompose;
using static BlazorCompose.Html;

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

For an element without a dedicated factory, use `Element` with a compile-time constant tag:

```csharp
Element("figure")[Img.Src("/diagram.png").Alt("Architecture")]
```

## Decorations

Decorations are chained onto the element they belong to. They collapse into the owning element's
attributes rather than introducing wrapper nodes:

```csharp
Button
    .Class("btn btn-primary")
    .Title("Save the current document")
    .OnClick(() => Save())["Save"];
```

Available decorations are `.Class`, `.Id`, `.Href`, `.Src`, `.Alt`, `.Type`, `.Title`, `.Role`,
`.OnClick`, and the general-purpose escape hatches `.Attr(name, value)` and `.On(eventName, handler)`.

A decoration must target a single element. Applying one to `If`, `ForEach`, `Fragment`, `Raw`, or a
component result reports diagnostic BC3008, because those constructs open no element to attach to.

## Next

Read [control flow](./control-flow.md) for conditionals and lists, or go back to
[getting started](./getting-started.md#installation).

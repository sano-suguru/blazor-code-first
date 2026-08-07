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
            Li["An HTML element is a helper, named by its tag with only the first letter uppercased — Figcaption, not FigCaption, likewise Colgroup and Textarea."],
            Li["Element covers what is not a helper: custom elements, Web Components, and the few excluded vocabularies below."]]];
```

A dedicated helper exists for every element the HTML Living Standard lists as conforming, so a
`<figure>` is one call away, attributes before children like any other element:

```csharp
Figure[
    Img.Src("/diagram.png").Alt("Architecture"),
    Figcaption["The compilation pipeline"]]
```

`Element` is what is left over: custom elements and Web Components, whose tag names are never a
known helper, and a handful of standard elements no helper covers — the document and
`<head>`-only elements (`html`, `head`, `body`, `title`, `base`, `meta`, `link`), raw-text elements
(`script`, `style`, `noscript`), elements the render tree cannot give meaning to (`template`,
`slot`), `object` (ambiguous with the C# keyword), and the foreign vocabularies `svg` and `math` in
full:

```csharp
Element("my-widget").Attr("value", "42")   // custom element
Element("svg")[Element("circle")]          // foreign vocabulary
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

## Void elements take no children

The thirteen void elements of the HTML standard — `area`, `base`, `br`, `col`, `embed`, `hr`, `img`,
`input`, `link`, `meta`, `source`, `track`, `wbr` — have no closing tag, so children written on one
report BCF3016:

```csharp
Img.Src("/logo.png")["Logo"]     // BCF3016
Element("img")["Logo"]           // BCF3016, same rule
Img.Src("/logo.png").Alt("Logo") // what to write instead
```

The reason is that the children do not survive a round trip through HTML. Prerendering serializes a
closing tag the HTML parser does not accept, so the parser pushes the children out of the element and
they appear as its siblings; a stray `</br>` is even re-read as a start tag, so `Br["x"]` prerenders
as two `<br>` elements. Interactive rendering has no parser in the way and puts the same children
inside the element. One expression, two different DOM trees, and the page changes shape as hydration
takes over. Configure a void element with decorations and put content beside it.

Both spellings are checked, the helper and `Element` with a void tag. Custom elements and unknown
tags are not: `Element("img-viewer")["child"]` is accepted, because there is no standard to read
their content model out of.

This is the limit of what the surface checks about HTML, and the limit is deliberate. BCF3016 is
decidable from the element tag by itself. Whether a particular child is allowed inside a particular
parent is not — `Table[Div["x"]]` also renders differently after hydration, and it is accepted, along
with attributes an element does not define (`Div.Href("/x")`). See `DESIGN.md` §4.1 for the whole
position.

## When a name collides

`using static BlazorCodeFirst.Html;` imports every conforming HTML element name, and a member of your
own component wins simple-name lookup over an imported one. Blazor parameters named `Label`, `Data`,
`Summary` or `Source` are ordinary, so this happens.

A type that shadows a helper names itself in the error:

    error CS0119: 'Table' is a type, which is not valid in the given context

A member whose type is indexable does not — the element expression silently becomes an indexer call
on your member:

```csharp
[Parameter] public string Data { get; set; }
Div[Data["Heading"]]
```

    error CS1503: Argument 1: cannot convert from 'string' to 'int'

Both are fixed the same way, by qualifying the element:

```csharp
Div[Html.Data["Heading"]]
```

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

`.Attr` takes a `string` or a `bool`. A `bool` is Blazor's conditional attribute: `true` renders the
attribute with an empty value, which is how HTML reads `disabled`, `checked` and `hidden` as set, and
`false` leaves the attribute out entirely.

```csharp
Input.Type("checkbox").Attr("checked", _agreed).Attr("disabled", _submitting)
```

There is deliberately no `object` overload. A value of any other type is formatted at render time
under the formatting thread's culture rather than the one your component ran under, so write it out
yourself, where the culture is a choice you can see:

```csharp
Div.Attr("tabindex", index.ToString(CultureInfo.InvariantCulture))
```

A handler written as `Action` or `Func<Task>` receives nothing. To read the event, give the lambda
parameter its type, and `.On` picks up the typed overload:

```csharp
Input.Type("text").Attr("value", _name)
     .On("oninput", (ChangeEventArgs e) => _name = e.Value?.ToString() ?? "")
```

Unlike Razor, the argument type is not inferred from the event name, so writing it on the parameter
is what selects the overload. `ChangeEventArgs` lives in `Microsoft.AspNetCore.Components`;
`MouseEventArgs`, `KeyboardEventArgs` and `FocusEventArgs` live in
`Microsoft.AspNetCore.Components.Web`, which a Blazor app already references. Nothing checks that
the type you name is the one the event delivers.

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

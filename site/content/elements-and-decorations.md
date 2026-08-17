---
title: Elements and Decorations
description: Every element you write names the element it produces. How attributes chain onto a helper, and how children go in brackets.
order: 40
group: write
---

BlazorCodeFirst mirrors HTML directly: every element you write in a `Body` expression names the
element it produces. There is no second set of widget names to learn on top of HTML, and no runtime
UI tree.

## Elements

An HTML element is a helper, named by its tag with only the first letter uppercased — `Figcaption`,
not `FigCaption`, likewise `Colgroup` and `Textarea`. Attributes chain onto it, children go in
brackets.

<!-- bcf-figure: ElementNames -->

```csharp
protected override View Body =>
    Figure[
        Img.Src("/diagram.png").Alt("Architecture"),
        Figcaption["The compilation pipeline"]];
```

```html
<figure>
    <img src="/diagram.png" alt="Architecture">
    <figcaption>The compilation pipeline</figcaption>
</figure>
```

A dedicated helper exists for every element the HTML Living Standard lists as conforming.

`Element` covers everything else. Two things use it. The first is custom elements and Web
Components, whose tag names are never a known helper. The second is the handful of standard elements
no helper covers:

- the document and `<head>`-only elements: `html`, `head`, `body`, `title`, `base`, `meta`, `link`
- raw-text elements: `script`, `style`, `noscript`
- elements the render tree cannot give meaning to: `template`, `slot`
- `object`, whose name is ambiguous with the C# keyword
- the foreign elements `svg` and `math`, in full

```csharp
private const string Widget = "my-widget";

Element(Widget).Attr("value", "42")        // custom element
Element("svg")[Element("circle")]          // foreign element
Element(_kind + "-widget")                 // BCF3009: not a constant
Element("my widget")                       // BCF3009: not spelled like a tag name
```

The tag has to be a compile-time constant spelled like a tag name, or
[BCF3009](./diagnostics.md#bcf3009) is reported.

## Children

A bare string converts to a text node, so there is no separate `Text()` construct. An element with
no children drops the brackets entirely.

<!-- bcf-figure: MixedChildren -->

```csharp
protected override View Body =>
    Div[
        "plain text, then ",
        A.Href("/docs")["a link"],
        Br,
        Img.Src("/logo.png").Alt("Logo")];
```

```html
<div>plain text, then <a href="/docs">a link</a><br><img src="/logo.png" alt="Logo"></div>
```

A Blazor `RenderFragment` sits in the child list like any other child:

```csharp
[Parameter] public RenderFragment? ChildContent { get; set; }

protected override View Body => Div["before", ChildContent];
```

The generator has to see each child as its own expression to give it a sequence number. Writing the
children as a nested collection literal is accepted, because C# expands it to the same call:

```csharp
Div[["a", "b"]]     // same as Div["a", "b"], which is the form to prefer
```

A child list the generator cannot read through reports
[BCF1003](./diagnostics.md#bcf1003). Use [`ForEach`](./control-flow.md#foreach-and-its-key) for
repetition instead.

## Void elements take no children

The thirteen void elements of the HTML standard have no closing tag, so children written on one
report [BCF3016](./diagnostics.md#bcf3016):

```csharp
Img.Src("/logo.png")["Logo"]     // BCF3016
Element("img")["Logo"]           // BCF3016, same rule
Img.Src("/logo.png").Alt("Logo") // what to write instead
```

Configure a void element with decorations and put content beside it. This is the limit of what the
surface checks about HTML, and the limit is deliberate: `Table[Div["x"]]` also renders differently
after hydration, and it is accepted, along with attributes an element does not define
(`Div.Href("/x")`). See `DESIGN.md` §4.1 for the full rationale.

## When a name collides

`using static BlazorCodeFirst.Html;` imports every conforming HTML element name, and a declaration
of your own wins simple-name lookup over an imported one. Blazor parameters named `Label`, `Data`,
`Summary` or `Source` are ordinary, so this happens:

```csharp
[Parameter] public string Data { get; set; }
Div[Data["Heading"]]                          // BCF3027
Div[Html.Data["Heading"]]                     // what to write instead
```

A type, a namespace, or a method of yours takes the name the same way, and each is
[BCF3027](./diagnostics.md#bcf3027), naming what it found.

## Decorations

Decorations are chained onto the element they belong to, before its children, the way HTML writes
attributes inside the tag. They collapse into the owning element's attributes rather than
introducing wrapper nodes, and `class` folds: chaining `.Class` more than once merges the values
into a single attribute.

<!-- bcf-figure: ChainedDecorations -->

```csharp
protected override View Body =>
    Button
        .Class("btn")
        .Class("btn-primary")
        .Title("Save the current document")["Save"];
```

```html
<button class="btn btn-primary" title="Save the current document">Save</button>
```

Available decorations are `.Class`, `.Id`, `.Href`, `.Src`, `.Alt`, `.Type`, `.Title`, `.Role`,
`.OnClick`, and the general-purpose escape hatches `.Attr(name, value)` and `.On(eventName, handler)`.

`.On` takes the full attribute name including the `on` prefix (`.On("onmouseenter", …)`); nothing is
prefixed for you, and a name without it reports [BCF3019](./diagnostics.md#bcf3019). The name given
to `.Attr` or `.On` must be a non-empty compile-time constant
([BCF3011](./diagnostics.md#bcf3011)).

`.Attr` takes a `string?` or a `bool`. A `bool` is Blazor's conditional attribute: `true` renders the
attribute with an empty value, which is how HTML reads `disabled`, `checked` and `hidden` as set, and
`false` leaves the attribute out entirely. Where the attribute is always present, write it bare, as HTML
does — the `bool` is for the conditional case.

```csharp
Input.Type("checkbox").Attr("checked")                    // <input type="checkbox" checked>
Button.Attr("disabled", _submitting)["Save"]              // conditional
```

A `null` string value leaves the attribute out too, so an attribute that carries a value only sometimes
needs no branch around the element:

```csharp
Span.Attr("title", _hasTip ? _tip : null)["Hover me"]
```

`null` and `""` are different values at every stage — frames, prerendered HTML, and a re-render — so
`""` gives you `title=""` and `null` gives you no `title` at all. When a re-render turns a value null,
Blazor removes the attribute from the element already in the DOM rather than replacing the element.
Every decoration that takes one value accepts `null` this way.

There is deliberately no `object` overload. A value of any other type is formatted at render time
under the formatting thread's culture rather than the one your component ran under, so write it out
yourself, where the culture is explicit:

```csharp
Div.Attr("tabindex", index.ToString(CultureInfo.InvariantCulture))
```

### Handlers

A handler written as `Action` or `Func<Task>` receives nothing. To read the event, give the lambda
parameter its type, and `.On` picks up the typed overload:

```csharp
Input.Type("text").Attr("value", _name)
     .On("oninput", (ChangeEventArgs e) => _name = e.Value?.ToString() ?? "")
```

Unlike Razor, the argument type is not inferred from the event name, so writing it on the parameter
is what selects the overload. `ChangeEventArgs` lives in `Microsoft.AspNetCore.Components`;
`MouseEventArgs`, `KeyboardEventArgs` and `FocusEventArgs` live in
`Microsoft.AspNetCore.Components.Web`, which a Blazor app already references.

The type is not inferred, but it is checked. Naming one the event does not deliver reports
[BCF3028](./diagnostics.md#bcf3028), read from the same `[EventHandler]` metadata Razor uses. A base
of the delivered type is accepted, because that is what the handler can actually receive:

```csharp
Button.On("onclick", (MouseEventArgs e) => Zoom(e.ClientX, e.ClientY))["Zoom"]   // the delivered type
Button.On("onclick", (EventArgs e) => Save())["Save"]                            // a base of it: fine
Button.On("onclick", (KeyboardEventArgs e) => Save())["Save"]                    // BCF3028
```

An event with no `[EventHandler]` registration has no mapping to check against, so a custom event you
have not registered is left alone. Registering one is the ordinary Blazor mechanism, and a
registration in your own project is read:

```csharp
[EventHandler("onrate", typeof(RatingEventArgs))]
public static class AppEventHandlers;
```

An attribute out and an event back is the pair [`.Bind`](./two-way-binding.md) writes as one
decoration.

### The class channel

Because that channel joins its values as text, `class` is the one name that takes a string and nothing
else. `.Attr("class", flag)` reports [BCF3023](./diagnostics.md#bcf3023) — as does `.Attr("class")`,
whose bare spelling stands for a presence and so has no text to join either. Write a conditional class
as a string, using `null` for the term you want gone:

```csharp
Div.Class("card").Class(_selected ? "is-selected" : "")
```

A `null` term drops out of the join, so an element carrying one class decoration loses the attribute
entirely when the term is null. It still leaves the separator behind when there is another term to join
against: `Div.Class("card").Class(_selected ? "is-selected" : null)` renders `class="card "` while
`_selected` is false, which the browser reads as the single class `card`.

Every other attribute and event is a single binding, and binding one twice on the same element reports
[BCF3010](./diagnostics.md#bcf3010). `style` is one of those others: write it as `.Attr("style", …)`.
`.Bind("class", …)` is the third way to write the name and the one that does not fold, so an element
carrying both it and a `.Class` reports [BCF3024](./diagnostics.md#bcf3024).

### Where a decoration may go

A decoration must target a single element. Applying one to `If`, `ForEach`, `Fragment`, `Raw`, or a
component result reports [BCF3008](./diagnostics.md#bcf3008), because none of those opens an
element. Writing the chain after the children (`Div["text"].Class("card")`) reports the same thing:
the brackets have already produced a `View`.

A decoration also has to be one this library declares. A misspelled name (`Div.Clas("card")`), or an
extension method of your own that takes an element and gives one back, reports
[BCF3026](./diagnostics.md#bcf3026).

## Next

Read [control flow](./control-flow.md) for conditionals and lists, or go back to
[getting started](./getting-started.md#installation).

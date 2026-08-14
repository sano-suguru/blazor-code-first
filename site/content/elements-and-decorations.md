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

`Element` is what is left over. Two things reach it. The first is custom elements and Web
Components, whose tag names are never a known helper. The second is the handful of standard elements
no helper covers:

- the document and `<head>`-only elements: `html`, `head`, `body`, `title`, `base`, `meta`, `link`
- raw-text elements: `script`, `style`, `noscript`
- elements the render tree cannot give meaning to: `template`, `slot`
- `object`, whose name is ambiguous with the C# keyword
- the foreign vocabularies `svg` and `math`, in full

```csharp
Element("my-widget").Attr("value", "42")   // custom element
Element("svg")[Element("circle")]          // foreign vocabulary
```

The tag has to be a non-empty compile-time constant, a literal or a `const`, or BCF3009 is reported.
`Element` lowers its tag to a literal `OpenElement`, and holding it to a constant is what keeps the
call as readable as a helper. The rule is about declarativeness rather than safety: a computed tag is
neither an injection risk nor a sequencing problem — it just stops the element from naming itself
where you wrote it.

```csharp
private const string Widget = "my-widget";

Element(Widget)                  // fine
Element(_kind + "-widget")       // BCF3009
Element("")                      // BCF3009, the tag must be non-empty
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

`using static BlazorCodeFirst.Html;` imports every conforming HTML element name, and a declaration of
your own wins simple-name lookup over an imported one. Blazor parameters named `Label`, `Data`,
`Summary` or `Source` are ordinary, so this happens.

A member whose type is indexable makes this legal C#: the element expression silently becomes an
indexer call on your member, and the generator reports BCF3027 to name it.

```csharp
[Parameter] public string Data { get; set; }
Div[Data["Heading"]]                          // BCF3027
```

A type, a namespace, or a method of yours takes the name the same way, and each is the same report,
naming what it found:

```csharp
public sealed class Table;                    // Table["x"]   — BCF3027, a type
namespace MyApp.Article { }                   // Article["x"] — BCF3027, a namespace
private string Summary() => "";               // Summary["x"] — BCF3027, a method
```

C# has an error for every one of these — CS1503 on the index argument, CS0119, CS0118, CS0021 — and
you never see any of them. As long as the body does not translate, the component has no generated
`RenderView`, so the compiler stops before it binds method bodies, which is where all four are found.

They are fixed the same way, by qualifying the element:

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
Every decoration that takes one value accepts `null` this way: `.Class`, `.Href`, `.Src`, `.Alt`,
`.Id`, `.Type`, `.Title`, `.Role`, and `.Attr`.

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
`Microsoft.AspNetCore.Components.Web`, which a Blazor app already references.

The type is not inferred, but it is checked. Naming one the event does not deliver reports BCF3028,
read from the same `[EventHandler]` metadata Razor uses — so `.On("onclick", (KeyboardEventArgs e) => …)`
stops at compile time instead of failing when the button is clicked. A base of the delivered type is
accepted, because that is what the handler can actually receive:

```csharp
Button.On("onclick", (MouseEventArgs e) => Zoom(e.ClientX, e.ClientY))["Zoom"]   // the delivered type
Button.On("onclick", (EventArgs e) => Save())["Save"]                            // a base of it: fine
Button.On("onclick", (KeyboardEventArgs e) => Save())["Save"]                    // BCF3028
```

A type that is not an `EventArgs` at all — `.On("onclick", (int x) => …)` — is the same diagnostic; C#
refuses that call outright, and BCF3028 is what names the reason. An event with no `[EventHandler]`
registration has no mapping to check against, so a custom event you have not registered is left alone.
Registering one is the ordinary Blazor mechanism, and a registration in your own project is read:

```csharp
[EventHandler("onrate", typeof(RatingEventArgs))]
public static class AppEventHandlers;
```

That pair — an attribute out, an event back — is what [`.Bind`](./two-way-binding.md) writes as one
decoration.

`class` is the one attribute that folds: chaining `.Class` more than once merges the values into a
single `class` attribute, and `.Attr("class", …)` joins the same channel. Every other attribute and
event is a single binding, and binding one twice on the same element reports BCF3010. `style` is one of
those others: write it as `.Attr("style", …)`, and note that two of them on one element is BCF3010
rather than a second fold.

Because that channel joins its values as text, `class` is the one name that takes a string and nothing
else. `.Attr("class", flag)` reports BCF3023 — as does `.Attr("class")`, whose bare spelling stands for
a presence and so has no text to join either. Write a conditional class as a string, using `null` for
the term you want gone:

```csharp
Div.Class("card").Class(_selected ? "is-selected" : "")
```

A `null` term drops out of the join, so an element carrying one class decoration loses the attribute
entirely when the term is null. It still leaves the separator behind when there is another term to join
against: `Div.Class("card").Class(_selected ? "is-selected" : null)` renders `class="card "` while
`_selected` is false, which the browser reads as the single class `card`.

`.Bind("class", …)` is the third way to write the name and the one that does not fold, so an element
carrying both it and a `.Class` would be emitted with `class` twice. That reports BCF3024, whichever
order they are written in. Bind it alone and let the getter supply the whole value.

A decoration must target a single element. Applying one to `If`, `ForEach`, `Fragment`, `Raw`, or a
component result reports diagnostic BCF3008, because those constructs open no element to attach to.
Writing the chain after the children (`Div["text"].Class("card")`) reports BCF3008 for the same
reason: the brackets have already produced a `View`.

A decoration also has to be one this library declares. A misspelled name (`Div.Clas("card")`), or an
extension method of your own that takes an element and gives one back, reports BCF3026. C# has an error
for the misspelling, but the same stop keeps it from you.

## Next

Read [control flow](./control-flow.md) for conditionals and lists, or go back to
[getting started](./getting-started.md#installation).

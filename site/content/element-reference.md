---
title: Element Reference
order: 90
group: reference
---

Everything this surface declares, in one place to look up rather than to read. What each construct
means is in [elements and decorations](./elements-and-decorations.md) and
[control flow](./control-flow.md).

## Elements

A helper is named by its tag with only the first letter uppercased: `Figcaption`, not `FigCaption`.
An asterisk marks a void element, which takes no children
([BCF3016](./diagnostics.md#bcf3016)).

There are 100 of them, one for every element the HTML Living Standard lists as conforming and that
the render tree can give meaning to.

### Sections

`Address`, `Article`, `Aside`, `Footer`, `H1`, `H2`, `H3`, `H4`, `H5`, `H6`, `Header`, `Hgroup`,
`Main`, `Nav`, `Search`, `Section`

### Grouping

`Blockquote`, `Dd`, `Div`, `Dl`, `Dt`, `Figcaption`, `Figure`, `Hr`\*, `Li`, `Menu`, `Ol`, `P`,
`Pre`, `Ul`

### Text-level

`A`, `Abbr`, `B`, `Bdi`, `Bdo`, `Br`\*, `Cite`, `Code`, `Data`, `Dfn`, `Em`, `I`, `Kbd`, `Mark`,
`Q`, `Rp`, `Rt`, `Ruby`, `S`, `Samp`, `Small`, `Span`, `Strong`, `Sub`, `Sup`, `Time`, `U`, `Var`,
`Wbr`\*

### Edits

`Del`, `Ins`

### Embedded

`Area`\*, `Audio`, `Canvas`, `Embed`\*, `Iframe`, `Img`\*, `Map`, `Picture`, `Source`\*, `Track`\*,
`Video`

### Tabular

`Caption`, `Col`\*, `Colgroup`, `Table`, `Tbody`, `Td`, `Tfoot`, `Th`, `Thead`, `Tr`

### Forms

`Button`, `Datalist`, `Fieldset`, `Form`, `Input`\*, `Label`, `Legend`, `Meter`, `Optgroup`,
`Option`, `Output`, `Progress`, `Select`, `Selectedcontent`, `Textarea`

### Interactive

`Details`, `Dialog`, `Summary`

### Everything else: `Element(tag)`

`Element("my-widget")` names an element no helper covers. That is custom elements and Web
Components, plus the standard elements deliberately left out: the document and `<head>` elements,
raw-text elements, `template` and `slot`, `object`, and the SVG and MathML vocabularies. The tag has
to be a compile-time constant spelled like a tag name
([BCF3009](./diagnostics.md#bcf3009)). See
[elements and decorations](./elements-and-decorations.md#elements) for the full list of what reaches
it and why.

## Decorations on an element

| Decoration | Writes |
| --- | --- |
| `.Attr(name, value)` | any attribute, by name |
| `.Class(value)` | the class channel, which folds ([the class channel](./elements-and-decorations.md#the-class-channel)) |
| `.Id(value)` | `id` |
| `.Title(value)` | `title` |
| `.Role(value)` | `role` |
| `.Type(value)` | `type` |
| `.Src(value)` | `src` |
| `.Alt(value)` | `alt` |
| `.Href(value)` | `href` |
| `.On(name, handler)` | any event, by name |
| `.OnClick(handler)` | `onclick` |
| `.PreventDefault()` | the preceding event's `preventDefault` |
| `.StopPropagation()` | the preceding event's `stopPropagation` |
| `.Bind(…)` | a two-way binding ([two-way binding](./two-way-binding.md)) |
| `.Key(value)` | a diffing key, no markup ([control flow](./control-flow.md#foreach-and-its-key)) |
| `.Ref(capture)` | an element reference, no markup |

The named ones are shorthands, not a separate mechanism: `.Id("x")` and `.Attr("id", "x")` produce
the same frame, and both count as the same channel
([BCF3010](./diagnostics.md#bcf3010)).

## Constructs

| Construct | Does |
| --- | --- |
| `If(condition, then, otherwise?)` | one branch, with disjoint sequence ranges |
| `ForEach(source, key, content)` | a keyed list |
| `Fragment(children…)` | groups children with no wrapper element |
| `Raw(html)` | injects trusted HTML verbatim |
| `Slot` | where a content-taking `[ViewPart]` places its caller's children |
| `Component<T>()` | calls a Blazor component |

## Decorations on a component call

| Decoration | Writes |
| --- | --- |
| `.Param(selector, value)` | one `[Parameter]` |
| `.Template(selector, template)` | a `RenderFragment<T>` parameter |
| `.Bind(selector, …)` | a two-way parameter binding ([two-way binding](./two-way-binding.md#binding-a-component-parameter)) |
| `.Key(value)` | a diffing key |
| `.RenderMode(mode)` | the call-site render mode ([installation and hosting](./installation-and-hosting.md#naming-a-render-mode)) |
| `.Ref(capture)` | a component reference |

Child content written in brackets sets `ChildContent`, and it counts as a binding of that parameter
([BCF3007](./diagnostics.md#bcf3007)).

## Base types

| Type | For |
| --- | --- |
| `BodyComponentBase` | a component, overriding `Body` |
| `ChromeLayoutBase` | a layout, overriding `Chrome` ([layouts](./layouts.md)) |
| `[ViewPart]` | a method whose markup expands into its caller |
| `SlotView` | the return type of a `[ViewPart]` that takes content |

---
title: FAQ
order: 110
group: reference
---

Questions this surface's shape raises, and where each answer is settled.

## Why is there no `VStack`, `.Padding()` or `Text()`?

Because this is not that kind of code-first. The surface mirrors HTML: every element you write is
the element it produces, CSS does the layout, and a bare string is a text node.

A second vocabulary would mean learning it, mapping it back to HTML when something renders wrong,
and maintaining it as HTML and CSS move. `DESIGN.md` §4.1 records the lineage this follows and why
the SwiftUI one was declined.

## Can I keep my `.razor` files?

Yes, in both directions. A `.razor` file names one of these components as an ordinary tag, and one
of these calls a `.razor` component through `Component<T>()`.

The one restriction is same-project: `Component<T>()` cannot name a `.razor` type declared in the
same project, because source generators cannot observe each other's output
([BCF3012](./diagnostics.md#bcf3012)). The same component in a referenced project resolves normally.
See [components and reuse](./components-and-reuse.md#calling-an-existing-razor-or-third-party-component).

## Does it work with MudBlazor, Radzen, or any other component library?

Yes. Those are ordinary Blazor components in referenced assemblies, which is exactly the case
`Component<T>()` is for. Parameters go through `.Param`, templates through `.Template`, and child
content in brackets.

## What does it cost at runtime?

Nothing beyond what a `.razor` component costs. The generator emits a `RenderTreeBuilder` method
with statically assigned sequence numbers, which is what the Razor compiler emits too.

There is no runtime UI tree, no reflection, and no expression compilation. The design-time API you
wrote the component with does not even ship: the IL trimmer removes it.

## Why must the class be `partial` and top-level?

`partial` because the generator writes the rendering into your class
([BCF1001](./diagnostics.md#bcf1001)). Top-level because generated code cannot reproduce a chain of
enclosing type declarations, including their type parameters
([BCF1005](./diagnostics.md#bcf1005)).

## Why can't I write a normal `if` or `foreach` in `Body`?

Because each one would need a sequence space of its own, and the whole point of the compilation is
that every template position gets its number at build time. `If` and `ForEach` are that same control
flow with the space made explicit.

The getter reaches one returned expression, with locals allowed ahead of it
([getting started](./getting-started.md#the-getter-reaches-one-expression)). If a body genuinely
cannot be written that way, override `RenderView` by hand.

## Why does `ForEach` make me pass a key?

Because a list that diffs by position reuses the wrong element state the moment it reorders, and
that is invisible until a user notices their input moved. The parameter has no default, so a list
either identifies its items or writes `key: null` and takes the cost knowingly
([declining the key](./control-flow.md#declining-the-key)).

## Is `Raw` safe?

Only for content you trust. It writes to the DOM without escaping, which is what `MarkupString`
does in Razor. Never flow user input or an external response through it
([Raw](./control-flow.md#raw)).

## The build stopped and I do not recognize the ID

Every diagnostic has an entry in the [reference](./diagnostics.md), giving what it means and what to
write instead. The build prints the ID; search that page for it.

## Where do I look when something renders differently from what I wrote

That is the case the compiler is built to prevent, so start with the diagnostics: it reports what it
cannot translate rather than emitting something that differs. If nothing fired, the difference is
between your expression and the HTML you expected, and
[elements and decorations](./elements-and-decorations.md) is where that correspondence is written
down.

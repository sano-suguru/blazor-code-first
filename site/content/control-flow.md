---
title: Control Flow
order: 30
---

Conditionals and lists are expressed with dedicated constructs so the generator can assign
compile-time sequence numbers to every position in the template.

## If

`If` takes a condition and a content thunk, with an optional else branch:

```csharp
protected override View Body =>
    Div[
        If(_items.Count == 0,
            () => P["Nothing here yet."],
            () => Span[$"{_items.Count} items"])];
```

Mutually exclusive branches get disjoint sequence ranges, so switching branches never disturbs the
sibling positions around them.

## Keyed ForEach

`ForEach` requires a key that identifies the item, not its position:

```csharp
protected override View Body =>
    Ul[
        ForEach(_items,
            key: item => item.Id,
            content: item => Li[item.Name])];
```

Sequence numbers identify template positions; keys identify data instances. Passing an index as the
key defeats the diff, because reordering the list makes Blazor reuse the wrong element state. The
content root must be a single element or component, so a `Fragment` or `Raw` root, or a
`RenderFragment` placed as content, reports BCF3003.

## Fragment

`Fragment` groups children without emitting a wrapper element, the equivalent of `<>...</>`:

```csharp
Fragment(H2["Title"], P["Body"])
```

Because it opens no element it cannot be decorated (BCF3008) and cannot be a `ForEach` content root
(BCF3003).

## Raw

`Raw` injects a trusted HTML string verbatim, the `MarkupString` equivalent. This page itself is
rendered that way: its Markdown is converted to HTML at build time and passed to `Raw`.

```csharp
Article.Class("prose")[Raw(entry.Html)]
```

`Raw` writes to the DOM without escaping, so it accepts trusted content only. Never flow user input
or an external response through it.

## Next

See [components and reuse](./components-and-reuse.md) for calling one component from another, or
[elements and decorations](./elements-and-decorations.md#decorations) for the element vocabulary.

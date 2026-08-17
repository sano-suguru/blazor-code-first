---
title: Control Flow
order: 50
group: write
---

Conditionals and lists are expressed with dedicated constructs so the generator can assign
compile-time sequence numbers to every position in the template.

## If

`If` takes a condition and a content thunk, with an optional else branch. A render is one state, so
the output below is the branch `_items.Length == 0` did not take:

<!-- bcf-figure: Conditional -->

```csharp
protected override View Body =>
    Div[
        If(_items.Length == 0,
            () => P["Nothing here yet."],
            () => Span[$"{_items.Length} items"])];
```

```html
<div><span>2 items</span></div>
```

Mutually exclusive branches get disjoint sequence ranges, so switching branches never disturbs the
sibling positions around them.

## ForEach and its key

`ForEach` takes a key that identifies the item, not its position. The key is not written to the
markup: it is a diffing instruction, not an attribute.

<!-- bcf-figure: KeyedList -->

```csharp
protected override View Body =>
    Ul[
        ForEach(_items,
            key: item => item.Id,
            content: item => Li[item.Name])];
```

```html
<ul>
    <li>Alpha</li>
    <li>Beta</li>
</ul>
```

Sequence numbers identify template positions; keys identify data instances. Passing an index as the
key makes the diff useless, because reordering the list makes Blazor reuse the wrong element state.

A key that never references its item is reported. All three of these report
[BCF3002](./diagnostics.md#bcf3002):

- `key: _ => 0`
- a key read from a counter outside the lambda
- in a nested loop, an inner key naming only the outer item

```csharp
ForEach(_groups, key: g => g.Id, content: g =>
    Div[ForEach(g.Items, key: i => g.Id, content: i => Span[i.Name])])   // BCF3002 on the inner key
```

It is a warning rather than an error and does not stop the component being emitted, because the list
still renders correctly and only diffs badly. The check is also deliberately conservative: it asks
whether the item was referenced, not whether the value identifies anything. A key derived from the
item but still position-like passes it. BCF3002 is a lower bound, not a guarantee.

Both lambdas have to be inline expression lambdas, so wrap a call instead of naming it —
`item => Row(item)` rather than `Row` ([BCF3004](./diagnostics.md#bcf3004)). The content root must
be a single element or component ([BCF3003](./diagnostics.md#bcf3003)), and it may not key itself as
well ([BCF3032](./diagnostics.md#bcf3032)).

## Declining the key

The key parameter has no default value, so a list that has no identity to key on says so:

```csharp
Ul[ForEach(_columns, key: null, content: c => Li[c.Header])]
```

That is the right spelling for a static menu, a fixed set of columns, or any projection that never
reorders. The cost is the one BCF3002 warns about: the list diffs as an index-derived key does, so
an insertion at the front rewrites every row and each row loses its local state. Because no `SetKey`
is emitted, BCF3002 has nothing to check and BCF3003 no longer applies — a `Fragment`, a `Raw`,
or a bare `If` may root the content.

## Splicing a projection

A child list can also take an ordinary projection, spread into it:

```csharp
Ul[[.. _columns.Select(c => Li[c.Header])]]
```

This is sugar for the declined-key `ForEach` above and compiles to the same `foreach`. Unlike a whole
child list, it mixes with siblings written around it:

```csharp
Ul[[Li["first"], .. _columns.Select(c => Li[c.Header]), Li["last"]]]
```

Only `<source>.Select(<inline expression lambda>)` folds. Any other spread — a stored array of
`View`, a method returning one — is not statically sequenceable children and reports
[BCF1003](./diagnostics.md#bcf1003), as a stored `View` written as a single child already does.

## Fragment

`Fragment` groups children without emitting a wrapper element, the equivalent of `<>...</>`:

```csharp
Fragment(H2["Title"], P["Body"])
```

Because it opens no element it cannot be decorated ([BCF3008](./diagnostics.md#bcf3008)) and cannot
be a `ForEach` content root ([BCF3003](./diagnostics.md#bcf3003)).

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

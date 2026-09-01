---
title: Control Flow
description: If and ForEach, the constructs that let the generator assign a compile-time sequence number to every position in a template.
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

## Spreading a `Select` into children

A child list can also take an ordinary projection — a `Select` — spread into it:

```csharp
Ul[[.. _columns.Select(c => Li[c.Header])]]
```

That is a second spelling of the declined-key `ForEach` above, and it compiles to the same `foreach`.
The items land in the order they are spread, among any children written by hand:

```csharp
Ul[[Li["first"], .. _columns.Select(c => Li[c.Header]), Li["last"]]]
```

Only `<source>.Select(<inline expression lambda>)` folds, and so does a call to an iterator
`[ViewPart]` — see [iterating with a ViewPart](#iterating-with-a-viewpart) below. Any other spread —
a stored array of `View`, a method returning one — is not statically sequenceable children and
reports [BCF1003](./diagnostics.md#bcf1003), as a stored `View` written as a single child already
does.

## Iterating with a `[ViewPart]`

A `[ViewPart]` can also be an iterator: a `static` method returning `IEnumerable<View>` whose body
is a native `foreach` ending, in its own last statement, in one `yield return` per iteration.

```csharp
[ViewPart]
private static IEnumerable<View> Rows(IReadOnlyList<Item> items)
{
    foreach (var item in items)
    {
        yield return Li.Key(item.Id)[item.Name];
    }
}
```

Splice a call to it the same way as any other spread:

```csharp
Ul[[.. Rows(_items)]]
```

This does not take the same path an ordinary `[ViewPart]` call does — expanding its body once per
call site. The number of items is a run-time fact, so a spliced iterator part instead reuses
`ForEach`'s own emission: one static content range, run once per iteration, the same as the
declined-key spread above rather than a copy of the body pasted per call.

`.Key(...)` is optional and is written on the yielded element itself, as an ordinary frame
decoration. It is the element's own key, not threaded through a separate `key:` argument the way
`ForEach`'s own key is — a native `foreach` header has no sibling slot to carry one. Omitting it
emits no `SetKey`, the same as `ForEach`'s own declined key.

`yield return` is the only spelling accepted here, and only on a `[ViewPart]`. A `foreach` ending in
`return` instead would exit after the first item rather than producing every one. C# allows
`yield return` only inside a genuine iterator — a method, never a property getter or a lambda. That
is why this shape is accepted only at a `[ViewPart]`'s own position, never where a plain
`foreach`/`if`/`switch` is written (see [BCF1002](./diagnostics.md#bcf1002)).

A `[ViewPart]` must still be `static`, the same as any other. Its body cannot read an instance field
directly, so the loop's own source is always taken as a parameter (`items` above), the same way any
other `[ViewPart]` argument is.

## Fragment

`Fragment` groups several children into one `View` without emitting a wrapper element:

```csharp
Fragment(H2["Title"], P["Body"])
```

Because it opens no element it cannot be decorated ([BCF3008](./diagnostics.md#bcf3008)) and cannot
be a `ForEach` content root ([BCF3003](./diagnostics.md#bcf3003)).

## Raw

`Raw` injects an HTML string verbatim, the `MarkupString` equivalent.

:::warning
`Raw` writes to the DOM without escaping. Pass it only content you produced yourself. A string that
came from a user, or back from another service, is parsed as HTML, and any script in it runs.
:::

This page is rendered through it. Its Markdown is converted to HTML at build time, by a tool in this
repository, and the result is passed to `Raw`.

```csharp
Article.Class("prose")[Raw(entry.Html)]
```

## Next

See [components and reuse](./components-and-reuse.md) for calling one component from another, or
[elements and decorations](./elements-and-decorations.md#decorations) for the element vocabulary.

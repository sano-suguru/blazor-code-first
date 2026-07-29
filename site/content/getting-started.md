---
title: Getting Started
order: 10
---

BlazorCompose lets you write Blazor UI as plain C#. This page is itself rendered
from Markdown, converted at build time and injected through `Html.Raw`.

## Installation

Add the runtime and the source generator to your project, then derive your
components from `ComposeComponentBase`.

## A first component

```csharp
using Microsoft.AspNetCore.Components;
using BlazorCompose;
using static BlazorCompose.Html;

[Route("/")]
public partial class Home : ComposeComponentBase
{
    protected override View Body =>
        Div(
            H1("Hello"),
            Span("Welcome to BlazorCompose."));
}
```

## Values copied into generated code

BlazorCompose copies design-time value expressions into a generated file that has no `using`
directives. Resolved type names are rewritten as `global::`-qualified names. If a type is still
unresolved and its spelling depends on the source file's lexical context, the generator reports
BC3015 at that type name.

Fix the name, fully qualify it, move a source-generated type to a referenced project, or replace it
with a hand-written C# type. A reference already rooted at `global::` is preserved and left to normal
C# resolution. Generic type arguments are checked independently.

## Next steps

- Read the [counter sample](/counter) to see events, `If`, and keyed `ForEach`.
- Jump to [Installation](#installation) or [A first component](#a-first-component).
- Learn the [element vocabulary](./elements-and-decorations.md) and [control flow](./control-flow.md#if).

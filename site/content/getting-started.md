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

## Next steps

- Read the [counter sample](/counter) to see events, `If`, and keyed `ForEach`.
- Jump to [Installation](#installation) or [A first component](#a-first-component).
- Learn the [element vocabulary](./elements-and-decorations.md) and [control flow](./control-flow.md#if).

---
title: Installation and Hosting
description: What to install, which target framework it needs, and which hosting decisions the library leaves to Blazor.
order: 20
group: start
---

One package, one target framework, and no configuration per hosting model. This page covers what to
install, and which decisions the library leaves to Blazor.

## Install

```
dotnet add package BlazorCodeFirst --prerelease
```

The published version carries a prerelease suffix. Without `--prerelease` the command looks for the
latest stable version, of which there is none, and resolves nothing.

One package carries both halves. The runtime is in `lib/net10.0`, and the generator and analyzers
are in `analyzers/dotnet/cs`. There is no second package to add.

## What you need

- .NET SDK 10.0.100 or later.
- Visual Studio 2026 version 18.0 or later, if you use an IDE.

The generator ships as a Roslyn 5.0 analyzer, and an older compiler refuses to load it.

`net10.0` is the baseline target and carries every feature.

## The hosting model is not this library's choice

Your component compiles to a `RenderView` override on an ordinary `ComponentBase` descendant. Blazor
diffs it exactly as it diffs a Razor component. Server, WebAssembly, and static server-side
rendering therefore work as they already do for `.razor`, and nothing here has to be configured per
model.

This documentation site is the worked example. It is a WebAssembly app, prerendered at build time
and published as static files.

## Naming a render mode

A call site can name the render mode of the component it calls:

```csharp
Component<Counter>().RenderMode(RenderMode.InteractiveWebAssembly)
```

That form is for a component that declares no mode of its own, which is when it is needed: the same
component rendered interactively from one page and statically from another. A component whose own
declaration fixes the mode rejects the call-site form
([BCF3034](./diagnostics.md#bcf3034)).

## Publishing trimmed

There is no runtime UI tree, no reflection, and no expression compilation, so nothing here needs a
trimming root or an AOT hint. The design-time API you wrote the component with is not in the
published output: the generator reads its syntax, and the IL trimmer removes it.

Binding is the one measured cost, and what it costs is published size: about 10 KB, once, rather
than per binding. See [two-way binding](./two-way-binding.md#if-you-publish-trimmed).

## Next

- Coming from `.razor`? Read [from Razor](./from-razor.md).
- Otherwise start at [getting started](./getting-started.md).

# BlazorCodeFirst

[![CI](https://github.com/sano-suguru/blazor-code-first/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/sano-suguru/blazor-code-first/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Blazor components written in C#, not `.razor`. HTML's own vocabulary becomes ordinary C#
expressions, compiled to `RenderTreeBuilder` calls at build time.

```csharp
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

[Route("/counter")]
public partial class CounterPage : BodyComponentBase
{
    // Stable identity keys (not indices) so the generator can diff the list safely.
    private static readonly List<IncrementStep> Steps = [new(1, 1), new(2, 5), new(3, 10)];

    private int _count;

    protected override View Body =>
        Div.Class("counter")[
            Span[$"Count: {_count}"],
            If(_count >= 3, () => Span["Milestone reached"]),
            Button.OnClick(() => _count++)["Increment"],
            ForEach(
                Steps,
                key: step => step.Id,
                content: step => Button.OnClick(() => _count += step.Amount)[$"+{step.Amount}"])];

    private sealed record IncrementStep(int Id, int Amount);
}
```

That page is `tests/BlazorCodeFirst.WebAppTestHost/Components/CounterPage.cs`. Run it with

```bash
dotnet watch --project tests/BlazorCodeFirst.WebAppTestHost/BlazorCodeFirst.WebAppTestHost.csproj
```

## How it works

A Roslyn source generator reads each component at build time and emits a standard Blazor
`RenderTreeBuilder` render method with statically assigned sequence numbers. The generated component
is an ordinary `ComponentBase` descendant. Blazor diffs it exactly as it diffs a Razor component, and
it stays trimming-safe and AOT-safe. There is no runtime UI tree, no reflection, and no expression
compilation.

The surface mirrors HTML:

- Elements are C# helpers named after their tags.
- Attributes and events sit next to the tag in a decoration chain.
- Children follow in brackets.
- CSS handles layout entirely.

This is not the SwiftUI or Jetpack Compose kind of code-first: there are no `VStack` / `HStack` /
`Grid` containers and no typed `.Padding()` / `.FontSize()` decorations. `DESIGN.md` §4.1 records
which lineage this follows, and why.

A `Body` is a typed C# expression. The compiler checks names and types, and refactorings propagate
through it like any other code. That is C# type checking, not validation of the HTML you wrote. The
analyzers catch what the type system cannot see, such as a component that forgets `partial`, state
mutated inside `Body`, or a duplicate attribute. Each is reported as a `BCF****` diagnostic during
the build.

## Status

Prerelease. The surface is deliberately narrow and grows one issue at a time. Not supported yet:
`preventDefault` / `stopPropagation`, `@ref` for elements and components, and form helpers.

A tag and an attribute name are always compile-time constants, so there is no `@attributes` splat
and no runtime-valued name. That is a decision rather than a gap: the class channel folds at compile
time and the duplicate check reads the name, and both go silent once the name is a value
(`DESIGN.md` §4.1).

## Installation

```bash
dotnet add package BlazorCodeFirst --prerelease
```

The published version carries a prerelease suffix, and without `--prerelease` the command resolves
the latest stable version instead, of which there is none. The single package carries both halves:
the runtime in `lib/net10.0`, and the generator and analyzers in `analyzers/dotnet/cs`.

Consuming the package needs .NET SDK 10.0.100 or later. In an IDE, it also needs Visual Studio 2026
version 18.0 or later. The generator ships as a Roslyn 5.0 analyzer, and an older compiler refuses
to load it. Building this repository is a separate requirement: `global.json` pins the SDK to
10.0.300.

## Documentation

- [Documentation site](https://blazor-code-first-site.snsgr.workers.dev): getting started, elements and
  decorations, control flow, two-way binding, components and reuse, layouts. The site itself is
  written in BlazorCodeFirst.
- [DESIGN.md](DESIGN.md) (Japanese): the design overview, covering background, goals, API design,
  and platform strategy. Start here.
- [ARCHITECTURE.md](ARCHITECTURE.md) (Japanese): the internal architecture, covering the compilation
  algorithm, static sequence assignment, memory layout, and analyzer diagnostics.
- [CONTRIBUTING.md](CONTRIBUTING.md): building, testing, diagnostics, and the issue-tracker
  conventions.

## License

MIT. See [LICENSE](LICENSE).

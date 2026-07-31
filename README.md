# BlazorCompose

A code-first declarative UI layer for Blazor — write your UI in pure C#, with no `.razor` markup
and no raw-string templates.

A Roslyn Source Generator analyzes your `Body` expressions and reachable `[Composable]` methods at
build time and emits a standard Blazor `RenderTreeBuilder` render method with statically assigned
sequence numbers. The generated component is an ordinary `ComponentBase` descendant, so it inherits
Razor's proven diffing performance and stays trimming/AOT-safe, with no runtime UI tree, reflection,
or expression compilation.

The vocabulary mirrors HTML: elements are C# factories, attributes and events are a postfix
decoration chain, and layout is left entirely to CSS. This puts BlazorCompose in the lineage of
kotlinx.html (Kotlin), ScalaTags (Scala), Feliz (F#), Elm's `html`, and hiccup (Clojure) rather than
of SwiftUI or Jetpack Compose — there are no `VStack` / `HStack` / `Grid` containers and no typed
`.Padding()` / `.FontSize()` decorations.

```csharp
using BlazorCompose;
using Microsoft.AspNetCore.Components;
using static BlazorCompose.Html;

[Route("/counter")]
public partial class CounterPage : ComposeComponentBase
{
    // Stable identity keys (not indices) so the generator can diff the list safely.
    private static readonly List<IncrementStep> Steps = [new(1, 1), new(2, 5), new(3, 10)];

    private int _count;

    protected override View Body =>
        Div(
            Span($"Count: {_count}"),
            If(_count >= 3, () => Span("Milestone reached")),
            Button("Increment").OnClick(() => _count++),
            ForEach(Steps, key: step => step.Id, content: step => Button($"+{step.Amount}").OnClick(() => _count += step.Amount)))
        .Class("bc-counter");

    private sealed record IncrementStep(int Id, int Amount);
}
```

That is `samples/BlazorCompose.Samples.Counter/Components/CounterPage.cs`, minus its namespace
declaration — the example is copied from a project that is built and tested in CI rather than
written for the README. Run it with
`dotnet watch --project samples/BlazorCompose.Samples.Counter/BlazorCompose.Samples.Counter.csproj`.

## What "type-safe" means here

`Body` is an ordinary typed C# expression, so names and types are checked by the compiler and
refactorings propagate through it like any other code. It is **not** compile-time validation of
HTML: every element is one unified node type carrying a string tag — hiccup / ScalaTags style, not
kotlinx.html style — so `Img("child")` accepts children and `.Href(…)` chains onto a `Div`. That is
the chosen position, recorded in `DESIGN.md` §4.1, not a gap to be closed.

What C# cannot check is the *shape* of a `Body`: a component that forgets `partial`, state mutated
inside `Body`, a decoration applied to something that is not a single element, a duplicate
attribute, a non-constant tag name. Those are enforced after the fact by the compiler's own
BC1xxx/BC3xxx diagnostics, listed in `ARCHITECTURE.md`.

## Status

Prerelease. `BlazorCompose` `0.1.0-dev` is not published to nuget.org; build it from this
repository (see [Installation](#installation)). The surface is deliberately narrow and grows by
issue.

Available today:

- 22 curated element helpers (`Div` `Span` `Button` `Nav` `Header` `Main` `Aside` `Footer`
  `Section` `Article` `P` `H1`–`H6` `Ul` `Ol` `Li` `A` `Img`), plus `Element(tag, …)` for any other
  tag, `Fragment(…)`, and `Raw(html)` for trusted HTML.
- Mixed children: bare strings and `View`s in the same list; a Blazor `RenderFragment` is also a
  child, which is how Razor-supplied content flows in.
- Decorations: `.Class` (folding), the `.Href` `.Src` `.Alt` `.Id` `.Type` `.Title` `.Role`
  shortcuts, generic `.Attr(name, value)`, and `.OnClick` / `.On(eventName, …)` with `Action` or
  `Func<Task>` handlers.
- Control flow: `If(condition, then, otherwise)` and keyed `ForEach(source, key, content)`.
- Razor interop in both directions: `Component<T>()` with `.Param(…)` and child content renders an
  existing Razor component, and a Compose component is an ordinary component that Razor can use.
- Layouts: `ComposeLayoutBase` with a `Chrome` expression.
- Reusable `[Composable]` methods, expanded statically into the caller.

Not covered yet — tracked as a single surface-area inventory in
[#72](https://github.com/sano-suguru/blazor-compose/issues/72): typed event arguments
(`MouseEventArgs` and friends), `bool` / `object`-valued attributes, `@bind`, `preventDefault` /
`stopPropagation`, attribute splatting, `@ref` for elements and components, form helpers, and the
elements outside the curated 22 (tables, form controls, `Strong` / `Em` / `Pre` / `Code`, …).

One surface question is open rather than merely unimplemented: whether attributes should stay
postfix or move ahead of children — [#73](https://github.com/sano-suguru/blazor-compose/issues/73).

## Installation

The package is not on nuget.org yet. Pack it locally:

```bash
dotnet pack src/BlazorCompose.Runtime/BlazorCompose.Runtime.csproj -c Release -o artifacts/package
```

That produces a single `BlazorCompose` `0.1.0-dev` package carrying both halves — the runtime in
`lib/net10.0` and the generator and analyzers in `analyzers/dotnet/cs`. Add `artifacts/package` as a
NuGet source and reference `BlazorCompose` `0.1.0-dev` from a `net10.0` Blazor project; the .NET SDK
is pinned to 10.0.300 in `global.json`.

## Documentation

- **[Documentation site](https://blazor-compose-site.pages.dev)** — getting started, elements and
  decorations, control flow, layouts. The site itself is written in BlazorCompose.
- **[DESIGN.md](DESIGN.md)** (Japanese) — design overview: background, goals, API design, and
  platform strategy. Start here.
- **[ARCHITECTURE.md](ARCHITECTURE.md)** (Japanese) — internal architecture: the compilation
  algorithm, static sequence assignment, memory layout, and analyzer diagnostics.
- **[CONTRIBUTING.md](CONTRIBUTING.md)** — building, testing, diagnostics, and the issue-tracker
  conventions.

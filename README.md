# BlazorCodeFirst

A code-first declarative UI layer for Blazor. You write your UI in pure C#, with no `.razor` markup
and no raw-string templates.

A Roslyn Source Generator analyzes your `Body` expressions and reachable `[Composable]` methods at
build time and emits a standard Blazor `RenderTreeBuilder` render method with statically assigned
sequence numbers. The generated component is an ordinary `ComponentBase` descendant, so it diffs the
way Razor's output does and stays trimming/AOT-safe. There is no runtime UI tree, no reflection, and
no expression compilation.

The vocabulary mirrors HTML: elements are C# helpers, attributes and events sit next to the tag in
a decoration chain, children follow in brackets, and layout is left entirely to CSS. That puts
BlazorCodeFirst in the lineage of kotlinx.html (Kotlin), ScalaTags (Scala), Feliz (F#), Elm's `html`,
and hiccup (Clojure) rather than SwiftUI or Jetpack Compose. There are no `VStack` / `HStack` /
`Grid` containers and no typed `.Padding()` / `.FontSize()` decorations.

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

That is `samples/BlazorCodeFirst.Samples.Counter/Components/CounterPage.cs` without its namespace
declaration, copied from a project CI builds rather than written for the README. Run it with
`dotnet watch --project samples/BlazorCodeFirst.Samples.Counter/BlazorCodeFirst.Samples.Counter.csproj`.

## What "type-safe" means here

`Body` is an ordinary typed C# expression, so the compiler checks names and types, and refactorings
propagate through it like any other code. It is not compile-time validation of HTML. Every element
is one unified node type carrying a string tag, hiccup and ScalaTags style rather than kotlinx.html
style, so `Img["child"]` and `Div.Href(…)` both type-check. `DESIGN.md` §4.1 records the position and
separates the two: `.Href(…)` on a `Div` renders as written and is not diagnosed, while `Img["child"]`
renders differently under static SSR and under interactive rendering, so it is rejected as BCF3016.
What is checked is the arity of the check, not validity: a break decidable from the element tag alone
can be diagnosed, while one that needs the (parent, child) pair — `Table[Div["x"]]` — is not.

What C# cannot check is the *shape* of a `Body`: a component that forgets `partial`, state mutated
inside `Body`, a decoration applied to something that is not a single element, a duplicate
attribute, a non-constant tag name. Those are enforced after the fact by the compiler's own
BCF1xxx/BCF3xxx diagnostics, listed in `ARCHITECTURE.md`.

## Status

Prerelease. `BlazorCodeFirst` `0.1.0-dev` is not published to nuget.org; build it from this
repository (see [Installation](#installation)). The surface is deliberately narrow and grows by
issue.

Available today:

- An element helper for every element the HTML Living Standard's element index lists as conforming,
  minus six reasoned exclusions recorded in `DESIGN.md` §4.1; `Element(tag)` still writes those, and
  custom elements, Web Components and foreign vocabularies. Plus `Fragment(…)` and `Raw(html)` for
  trusted HTML.
- Form elements are among them and are writable, but a working form cannot be built yet: two-way
  binding ([#71](https://github.com/sano-suguru/blazor-code-first/issues/71)) and typed event
  arguments ([#72](https://github.com/sano-suguru/blazor-code-first/issues/72)) do not exist, so an
  `Input` can be rendered but its value cannot be read back or bound.
- Mixed children, supplied in brackets after the tag and its attributes (`Div.Class("card")[…]`):
  bare strings and `View`s in the same list; a Blazor `RenderFragment` is also a child, which is how
  Razor-supplied content flows in.
- Decorations: `.Class` (folding), the `.Href` `.Src` `.Alt` `.Id` `.Type` `.Title` `.Role`
  shortcuts, generic `.Attr(name, value)`, and `.OnClick` / `.On(eventName, …)` with `Action` or
  `Func<Task>` handlers.
- Control flow: `If(condition, then, otherwise)` and keyed `ForEach(source, key, content)`.
- Razor interop in both directions: `Component<T>()` with `.Param(…)` and child content renders an
  existing Razor component, and a BlazorCodeFirst component is an ordinary component that Razor can use.
- Layouts: `ChromeLayoutBase` with a `Chrome` expression.
- Reusable `[Composable]` methods, expanded statically into the caller.

Not covered yet, tracked as a single surface-area inventory in
[#72](https://github.com/sano-suguru/blazor-code-first/issues/72): typed event arguments
(`MouseEventArgs` and friends), `bool` / `object`-valued attributes, `@bind`, `preventDefault` /
`stopPropagation`, attribute splatting, `@ref` for elements and components, and form helpers.

## Installation

The package is not on nuget.org yet. Pack it locally:

```bash
dotnet pack src/BlazorCodeFirst.Runtime/BlazorCodeFirst.Runtime.csproj -c Release -o artifacts/package
```

That produces a single `BlazorCodeFirst` `0.1.0-dev` package carrying both halves: the runtime in
`lib/net10.0`, and the generator and analyzers in `analyzers/dotnet/cs`. Add `artifacts/package` as
a NuGet source and reference `BlazorCodeFirst` `0.1.0-dev` from a `net10.0` Blazor project.

Consuming the package needs .NET SDK 10.0.100 or later, and Visual Studio 2026 version 18.0 or later
where the IDE is used: the generator ships as a Roslyn 5.0 analyzer, and an older compiler refuses
to load it. That is a separate requirement from building this repository, which pins the SDK to
10.0.300 in `global.json`.

## Documentation

- [Documentation site](https://blazor-code-first-site.pages.dev): getting started, elements and
  decorations, control flow, layouts. The site itself is written in BlazorCodeFirst.
- [DESIGN.md](DESIGN.md) (Japanese): the design overview, covering background, goals, API design,
  and platform strategy. Start here.
- [ARCHITECTURE.md](ARCHITECTURE.md) (Japanese): the internal architecture, covering the compilation
  algorithm, static sequence assignment, memory layout, and analyzer diagnostics.
- [CONTRIBUTING.md](CONTRIBUTING.md): building, testing, diagnostics, and the issue-tracker
  conventions.

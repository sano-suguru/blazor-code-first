# BlazorCodeFirst

Write Blazor components as ordinary C# instead of `.razor` markup. A Roslyn source generator folds
each `Body` into `RenderTreeBuilder` calls at build time, so there is no UI tree to interpret at run
time, no reflection, and no expression compilation.

You write this:

<!-- readme-example: input -->

```csharp
using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

[Route("/notices")]
public partial class NoticePage : BodyComponentBase
{
    private int _seen;

    protected override View Body =>
        Div.Class("notice")[
            H2["Release notes"],
            P["Nothing new today."],
            Button.OnClick(() => _seen++)[$"Seen {_seen} times"]];
}
```

The generator emits this, and it is what ships:

<!-- readme-example: generated -->

```csharp
partial class NoticePage
{
    protected override void RenderView(global::Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder __builder)
    {
        __builder.OpenElement(0, "div");
        __builder.AddAttribute(1, "class", "notice");
        __builder.AddMarkupContent(2, "<h2>Release notes</h2><p>Nothing new today.</p>");
        __builder.OpenElement(3, "button");
        __builder.AddAttribute(4, "onclick", global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _seen++));
        __builder.AddContent(5, $"Seen {_seen} times");
        __builder.CloseElement();
        __builder.CloseElement();
    }
}
```

What the pair shows:

- The two static siblings collapsed into one `AddMarkupContent` frame.
- The sequence numbers are literals the generator assigned, not values counted while rendering.
- `NoticePage` is an ordinary `ComponentBase` descendant, so it diffs the way Razor's output does
  and stays trimming- and AOT-safe.

## The vocabulary mirrors HTML

Elements are C# helpers named after their tags. Attributes and events sit next to the tag in a
decoration chain, children follow in brackets, and layout is left entirely to CSS. That puts
BlazorCodeFirst in the lineage of kotlinx.html, Scalatags, Feliz and hiccup rather than SwiftUI or
Jetpack Compose: there are no `VStack` / `HStack` / `Grid` containers and no typed `.Padding()` /
`.FontSize()` decorations.

`Body` is a typed C# expression, so the compiler checks names and types and refactorings propagate
through it. What C# cannot check is the shape of a `Body`: a component that forgets `partial`, state
mutated inside `Body`, a decoration applied to something that is not a single element, a duplicate
attribute, a non-constant tag name. The analyzers report those as `BCF****` diagnostics during the
build.

## Requirements

.NET SDK 10.0.100 or later, and a `net10.0` Blazor project. The generator ships as a Roslyn 5.0
analyzer, so an older compiler refuses to load it; where the IDE is used, that means Visual Studio
2026 version 18.0 or later.

## Installation

```
dotnet add package BlazorCodeFirst
```

This is a prerelease, and the surface is deliberately narrow and grows by issue. One package carries
both halves: the runtime in `lib/net10.0`, and the generator and analyzers in `analyzers/dotnet/cs`.

## Documentation

- [Documentation site](https://blazor-code-first-site.pages.dev): getting started, elements and
  decorations, control flow, layouts. The site is itself written in BlazorCodeFirst.
- [Repository](https://github.com/sano-suguru/blazor-code-first): the design overview
  (`DESIGN.md`), the compilation algorithm and the diagnostic table (`ARCHITECTURE.md`), and the
  issue tracker.

## License

MIT.

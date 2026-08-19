# BlazorCodeFirst: A Code-First Declarative UI Library for Blazor

**Design Overview: Background, Goals, and Design Policy**

Target platform: .NET 10 (LTS) baseline / .NET 11 multi-targeting

> For the formal definition of the internal mechanism, see `ARCHITECTURE.md`.
>
> In this document and in `ARCHITECTURE.md`, **the author** refers to the person writing UI on this surface. The person writing the library itself is the **maintainer**, and the person operating the app is the **end user**. This definition exists because reading "the author" at face value could mean the library's own author, and a sentence such as `DESIGN.md` §5.3's "the author never reads it" inverts its meaning depending on which is meant.

---

## 1. Overview

### 1.1 Background

Today, the mainstream of mobile and desktop UI development has shifted toward code-first declarative UI (Code-Driven Declarative UI), typified by SwiftUI, Jetpack Compose, and Flutter. These eliminate an external markup language such as HTML/XML, and build UI by leveraging the programming language's own features — type safety, autocomplete, refactoring, and inline logic.

Microsoft Blazor, meanwhile, is an excellent declarative framework, but fundamentally depends on Razor syntax (markup-first). The API for writing UI in C# alone (`RenderTreeBuilder`) is designed as a low-level, framework-internal facility, and is too verbose for a human to read and write. Microsoft itself does not recommend hand-writing `RenderTreeBuilder`, mainly because manually managing sequence numbers easily breaks diff detection.

### 1.2 Approach

BlazorCodeFirst is a library that introduces type-safe, code-first UI construction on top of Blazor.

BlazorCodeFirst's core is a compilation strategy of the same shape as the Razor compiler's. The Razor compiler generates a C# rendering method from `.razor` markup; BlazorCodeFirst's Source Generator, in the same way, generates a rendering method from the declarative `Body` expression the author wrote in C#. By making a C# expression, rather than markup, the source of truth, it carries over Razor's proven static sequence assignment and diff-detection performance unchanged, while achieving an authoring experience equivalent to SwiftUI or Jetpack Compose.

### 1.3 Technical decisions

The UI definition (`Body`) is the design-time source of truth, and is never evaluated at run time. The Source Generator parses `Body`'s expression tree and generates, into a partial class, a rendering method with static sequence numbers embedded as constants. This is the same shape the Razor compiler's own approach takes, and structurally satisfies what Blazor's diff detection requires — that sequence numbers be settled at compile time (§5).

This approach resolves, at the same time, two type-system obstacles that have traditionally troubled code-first UI. One is a unified return type. C# has no opaque return type equivalent to SwiftUI's `some View`, so there is no way to uniformly return a decoration chain's composed type (something like SwiftUI's `Padded<VStack<...>>`). Because this approach never evaluates at run time, every API need only return the lightweight marker type `View`. The other is the heap allocation a runtime tree-construction approach cannot avoid — this approach never allocates in principle, since the generated code issues instructions directly to `RenderTreeBuilder`.

The scope of analysis is explicitly specified: inside the statically analyzable syntax subset (SSC), every sequence is assigned statically; outside it (an unanalyzable method call, for instance), it degrades to a dynamic region while still preserving correctness (§5.3).

The platform strategy prioritizes LTS: net10.0 is the baseline, and net11.0 is opt-in multi-targeting (§3). Performance figures are presented with measured values and predictions clearly distinguished (§7).

### 1.4 Relationship to Razor, and this library's position

Why build this library when Razor already exists? As a starting premise: for declarative UI that targets the DOM, Razor — which aligns its syntax with the ready-made set of HTML tags (`div` / `span` / `ul`, and so on) — is the syntactically most straightforward default answer. This library accepts that fact, and deliberately aims at a different design point: pure C#, code-first.

The basis for that difference lies in language features. SwiftUI and Jetpack Compose can write `if` / `for` directly inside UI, because Swift/Kotlin carry result builders (`@ViewBuilder`) and trailing lambdas as language features. C# has none of this. Strictly, what is missing is not the whole of result builders so much as a mechanism that converts a statement (`if` / `foreach`) directly into a child-generating expression; a flat sequence of children can already be expressed with `params` or a collection initializer. That conditionals and lists in this library go, for now, through the `If()` / `ForEach()` combinators (§4.2) is because this surface makes up for that missing piece by an out-of-language means — the Source Generator. The path that handles a plain `if` / `foreach` is implemented incrementally (§5.3).

The reason for choosing this design, which concedes a step in syntactic straightforwardness, is not appearance — it lies in the following real value. Because UI and logic both close within the same C#, the context switch between markup and code disappears. Because UI is a plain C# expression, IDE rename and extract refactorings apply as-is, and type mismatches are caught at build time. There is also none of the tooling-accuracy degradation Razor tends to suffer at the boundary it straddles between markup and code. And UI can be assembled with ordinary functions, generics, and collection operations.

This library aims to gain these values without giving up Razor's proven static sequence assignment and diff-detection performance (§5). This library's position, then, is neither "a replacement for Razor" nor "superior to Razor" — it is a different option, sharing the same performance characteristics, aimed at an author who prioritizes a single language, type safety, and programmatic composition.

---

## 2. Core concepts

### 2.1 Pure C#, with HTML eliminated

HTML tag notation (enumerating tags in a markup file) is eliminated, and everything is expressed as C# methods, type-safe enums, and structs. Specifying a CSS class itself is a deliberate raw-string escape hatch: the string passed to `.Class(string)` flows straight into the `class` attribute (§4.1). IDE IntelliSense works, and layout or style errors are caught at build time.

### 2.2 Seamless integration with existing Blazor

This interoperates with the existing Blazor ecosystem. UI built with BlazorCodeFirst can be exposed as a standard `RenderFragment`, so it can be called from within an existing `.razor` component, and the reverse is possible too (§6).

### 2.3 Compilation equivalent to Razor's

The declarative expression written in `Body` is compiled by the Source Generator into a rendering method at build time. The generated code is in the same form as the Razor compiler's output — a sequence of `RenderTreeBuilder` instructions carrying static sequence numbers. From the Blazor engine's view, a BlazorCodeFirst component and an ordinary Razor component are indistinguishable. There is no runtime dynamic interpretation and no intermediate tree standing between the code the author writes and the instruction sequence that executes.

> A design requirement: a component class that declares an override of a design-time expression (`Body` / `Chrome`) must be declared `partial` (because the Source Generator generates the rendering method into the same class). A non-partial class is a build error (BCF1001). This is not required for a class that merely inherits a BlazorCodeFirst base without declaring a design-time expression. A nested class is not supported (BCF1005). A generic component (`partial class Foo<T>`) is supported.

---

## 3. Target platform strategy

| Target | Position | Features provided |
| ---------- | ------------------ | -------------------------------------------------------------------------------------------------------- |
| net10.0    | Baseline (required) | The full core engine. LTS (3-year support), a low adoption barrier for enterprise users |
| net11.0    | Opt-in (recommended) | The closed-world `ViewNode` definition via C# 15's union types and the `closed` hierarchy; a lighter event pipeline via Runtime Async |

This library's core technology (Source Generator member generation into a partial class) is a mature, standard feature, and depends on no particular bleeding-edge language feature. On net11.0 (planned GA November 2026, STS, 24-month support), C# 15's union types and the `closed` hierarchy let the set of UI nodes be defined as a closed discriminated union, with visitor exhaustiveness verified at compile time. The corresponding API is provided conditionally, behind `#if NET11_0_OR_GREATER`.

> Note: as of this writing (.NET 11 Preview 5), union types leave some features unimplemented, so the net11.0-targeted API is formalized only after GA.

---

## 4. API design and syntax specification

This chapter presents the surface's design policy and representative examples. An exhaustive how-to guide belongs to the documentation site, and each diagnostic's firing conditions belong to `ARCHITECTURE.md`'s Appendix A.

### 4.1 The structure of a basic component

The author defines the UI structure in a `partial` class that inherits `BodyComponentBase`, overriding the `Body` property. The vocabulary takes an "HTML-mirror surface," one that copies HTML elements straight across. This surface joins the lineage of kotlinx.html (Kotlin) / ScalaTags (Scala) / Feliz (F#) / Elm html (Elm) / hiccup (Clojure) — none of these invent their own layout vocabulary; each expresses the HTML element and attribute vocabulary directly as a language feature, and hands layout entirely to CSS.

```csharp
using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

public partial class CounterPage : BodyComponentBase
{
    private int _count;

    protected override View Body =>
        Div.Class("counter")[
            Span[$"Count: {_count}"],
            Button.OnClick(() => _count++)["Increment"],
            Button.OnClick(() => _count = 0)["Reset"]];
}
```

The surface's outline is as follows.

- Element helpers and combinators are collected into the static class `Html`. The recommended form is to bring in `using static BlazorCodeFirst.Html;` and write unqualified, as in `Div[...]`. Only where an imported name collides with an identifier common around Blazor, such as `Component` or `Element`, is the colliding spot alone qualified, as `Html.Component<T>()`.
- The curated element helpers give, as properties, every element listed as conforming in the HTML Living Standard's *Index — Elements*, minus the six groups excluded below. Because the exclusion is defined by reason rather than by enumeration, a new standard element automatically becomes a candidate. The canonical table of helper names to tag names lives in `KnownSymbols.CuratedTags`, and `KnownSymbolsSyncTests` pins its agreement with the runtime-side declarations in both directions. This document carries no copy of it.
- The six excluded groups, and the reason for each, are as follows.
  1. Document-skeleton and `<head>`-only elements (`html` / `head` / `body` / `title` / `base` / `meta` / `link`). These have meaning only inside `<head>` or as the document skeleton. A component's `Body` is drawn inside `<body>`, so these are silently inert there — no error, no effect. `body` does not even resolve as intended: inside a layout, `Body` resolves to `LayoutComponentBase.Body` instead, compiles, and silently returns the routed page. The path into `<head>` is `Component<HeadContent>()`, where `Element("meta")` can be used.
  2. Elements whose content model is raw text (`script` / `style` / `noscript`). Their children are raw text, not markup, so a surface that passes children through brackets would misrepresent what these elements contain.
  3. Elements whose meaning cannot reach through the render tree (`template` / `slot`). Measured on Chromium: adding a child to `<template>` makes `childNodes` 1 and `content.childNodes` 0 (the reverse of going through the parser, where only the parser ever populates `content`). Blazor never creates a shadow root, so nothing ever fills a `<slot>`. Both type-check and mean nothing.
  4. An element that becomes ambiguous in C# (`object`). CS0229 against the `object` keyword (measured).
  5. A foreign vocabulary (`svg` / `math`, and each of their element indices). The naming rule described below does not hold for these (SVG has `clipPath` / `linearGradient` / `feGaussianBlur`); including them would break an invariant the tests hold. `Math` also collides with `System.Math`, since the Razor SDK carries an implicit `using System;`. Curating only the root would just reproduce the same split this decision removed.
  6. Retired, non-conforming elements (`marquee` / `font` / `center` / `big` / `strike` / `tt` / `param` / `frameset` / `frame`, and the rest of the standard's "Obsolete features"). The standard has already removed these — this is the group the rule's word *conforming* exists for.
- An excluded element can still be written as-is: `Element("script")` / `Element("svg")` / `Element("object")` / `Element("template")`. No capability is lost. `Element(string tag)`, then, is not the ordinary path for writing a standard tag — it becomes the syntax for a custom element, a Web Component, a foreign vocabulary, and the six groups above. Both a curated helper and `Element` fall to the same unified node, and the two together are called an element helper (a term that implies neither a property nor a method, since it covers both).
- The naming rule is to capitalize only the tag name's first letter, and reformat nothing further: `Figcaption`, not `FigCaption`; likewise `Colgroup` / `Optgroup` / `Datalist` / `Textarea` / `Blockquote` / `Fieldset` / `Hgroup`. A conforming HTML tag name is all lowercase with no separators, so this rule holds without exception across every curated helper — and it holds because group 5 was excluded: SVG and MathML element names are not all lowercase, and including them would break both the rule and the test invariants built on top of it. So the correspondence between helper name and tag name is a bijection; the inverse map is lowercasing the first letter, with no per-tag mapping to memorize (`KnownSymbolsSyncTests` pins the inverse side). The option of making the helper names all lowercase, matching the tag letter for letter, was rejected on top of a measurement that found zero collisions with C#'s reserved words (`ARCHITECTURE.md` Appendix B.15).
- An attribute and an event are given not alongside the children but through a decoration chain directly after the tag (`Div.Class("card")["text"]`). Children are passed in the following `[...]`, mixing strings and `View`s; a plain string becomes a text node via implicit conversion, so there is no dedicated `Text()` syntax. A Blazor `RenderFragment` becomes a child as-is too. This positioning has a consequence beyond mere spelling preference (investigated 2026-08-09, #175): Lucid / Giraffe.ViewEngine / Falco.Markup place attributes in the same value namespace as children, and so respell attribute identifiers with a `_` prefix or suffix (`class_` / `_class_`). Placing attributes after the `.` is what lets `using static Html` work with no respelling of names — this backs up the placement #73 and #87 settled.
- `If` / `ForEach` (§4.2), `Component<T>()` (§6.2), `Fragment`, and `Raw` are syntax with no mapping to an HTML element. `Fragment` is wrapperless grouping; `Raw` injects a trusted HTML string directly into `RenderTreeBuilder.AddMarkupContent`. `Raw`'s trust boundary is the same as `MarkupString`'s, and passing an untrusted string through it becomes an XSS vector. Neither opens a single element frame, so neither can take a key or be decorated. A content-taking `[ViewPart]` call's (§4.3) `SlotView` return cannot be decorated for the same reason — but this is closed by the type, not a diagnostic: since every decoration is an extension method on `ElementView`, `Card("t").Class("x")` is CS1929, just like `Div["x"].Class("y")`.
- A layout inherits `ChromeLayoutBase`, and writes the design-time expression the layout itself draws in `Chrome`. The `Body` parameter Blazor requires of a layout (`LayoutComponentBase.Body`) is `RenderFragment?`, which converts implicitly to `View`, so it can be placed directly as an element's child, as in `Main[Body]`.
- Where this design positions type safety is not kotlinx.html's stance — a per-element type, content model, and attribute-applicability check enforced at compile time — but hiccup's / ScalaTags's, which take a unified node and a string tag. So the "type safety" this approach claims is at the C# level (the whole of `Body` is a typed C# expression, and composition and refactoring propagate through the type), and does not include an HTML-validity-level check. `Div.Href("/x")` / `Div.Attr("href", "/x")` / `Span[Div["x"]]` all emit exactly what was written, with no diagnostic.
- What is checked is not validity but a translation break. A shape where a design-time tree, serialized and parsed back, does not return to itself produces a different DOM under prerendering than under interactive rendering. The boundary is drawn at whether checking it would commit this repository to authoring and maintaining a table the check depends on (revised 2026-08-11, #155). It is checked when doing so is only copying a table the standard or the framework already ships as canonical; it is not checked when doing so would newly create an enumeration someone has to keep interpreting and adding to. For elements, the line this standard draws lines up exactly with whether the judgment is decided by a unary predicate over the element tag, or requires the binary relationship (parent, child). The unary side — whether an element can take children — is part of the content model, but is finite, stable, and mechanically derivable from the standard's own index. The binary side brings in interpretation at a scale quadratic in the number of elements, and mixes with the parser's normalization of otherwise-correct code, so that table can only be authored here. The term count is not the standard itself — it is the reason the standard draws its line there. This is the same logic behind defining the curated element set by rule rather than by enumeration.
- The unary side's first case is giving a void element children (BCF3016). The target is the 13 elements the HTML Living Standard lists as void elements, defined by rule the same way as the curated set; this document carries no enumeration of them (the list and firing conditions are in Appendix A A.1). The canonical table lives in `KnownSymbols.VoidTags`, and `KnownSymbolsSyncTests` pins its agreement with the curated and exclusion tables. Both a curated helper and `Element` fall to a single tag string before consulting the same table, so the two paths agree structurally, not by coincidence. Measured 2026-08-03 (net10.0 / ASP.NET Core 10.0.10): for `Img["child"]`, static SSR emits `<img src="/a.png">child</img>`. The HTML parser pushes the child out to a sibling text node, so under prerendering the child appears outside the element. Interactive rendering attaches a text node as `<img>`'s child for the same expression. `Br["child"]` gets its stray `</br>` reinterpreted as an opening tag, so `<br>` goes from one to two. `base` / `link` / `meta`, from exclusion group 1, are also void, so giving them children via `Element` falls under this check — group 1 was excluded because it is silently inert when written *without* children, a separate matter from being given children.
- The same standard reaches beyond elements. A mismatch between an event name and its handler's argument type is exactly that, and is checked as BCF3028 (decided 2026-08-11, #155). `.On("onclick", (KeyboardEventArgs e) => …)` compiles; the name and type stay mismatched, and it breaks at run time. Razor rejects the same shape, because it carries a correspondence table, `[EventHandler("onclick", typeof(MouseEventArgs), …)]`. This table is metadata the framework already ships as canonical, and reading it adds not a single enumeration for anyone to keep maintaining — so it sits inside the standard above, and there is no reason this surface should fall short of the check Razor performs. The test is assignability, not equality: `.On("onclick", (EventArgs e) => …)` stays valid. An event with no correspondence is not reported. Only the framework's correspondence table becomes unreadable in a compilation that does not reference `Components.Web`; a registration the author wrote themselves is still read there too (the two firing shapes and their sources are in Appendix A A.1).
- Measurement has found members on the unary side beyond void, and none of them are checked either. The binary side is not checked at all: it would commit this repository to authoring and maintaining a (parent, child) content-model table, and besides, the binary side includes shapes that are not even a mistake in how they were written — not even something a diagnostic could fix (`Table[Tr[Td["x"]]]` is written correctly, yet the parser inserts `tbody`, so the two paths diverge anyway). This document carries no enumeration of these (the list of measured residue, and which side each item belongs to, is in `ARCHITECTURE.md`'s Appendix D). That list is a record of positions chosen, not a work list to fill in.
- Whether an attribute may be applied is not checked (decided 2026-08-14, #335). `Div.Href("/x")` and `Div.Attr("href", "/x")` write an attribute `div` does not take, and neither is reported: this surface checks a translation break, not HTML validity, and both spellings still emit exactly what was written (`<div href="/x">`, under both static SSR and interactive rendering). `ARCHITECTURE.md` Appendix B.19 has the full argument.
- Every type on the design-time surface is a lightweight marker type (an empty `readonly struct`). An element name returns `ElementView`, `Component<T>()` returns `ComponentView<T>`, and a decoration takes `ElementView` and returns `ElementView`. A child-taking indexer and a combinator return `View`, and both `ElementView` / `ComponentView<T>` convert implicitly to `View`. This chain of types is what makes the attribute-then-child order a type-system requirement — the reverse order (`Div["text"].Class("card")`) does not work, since the decoration's receiver would be `View` (BCF3008).
- A state reference, an interpolated string, and an event lambda are transplanted into generated code whole, as syntax (within the same partial class, so access to a `private` member is preserved). During transplant, a resolved type name is normalized to its fully-qualified form starting with `global::`; an unresolved type name that depends on lexical context cannot be transplanted safely, and becomes BCF3015.
- Structural limits remain on the mirror. Each is a shape that has somewhere to map on the HTML side with no image on the surface — capitalization is not one of them (the bijection above). First, for the element vocabulary: the split HTML never had — curated tags versus everything else via `Element("…")` — has been removed, but the six excluded groups, each with a reason, remain, so the mirror is not total. Second, the attribute vocabulary is open to begin with (`data-*` / `aria-*` / custom attributes); with nothing to map as a closed set, `.Attr(name, value)` stays the general-purpose path on the attribute side, and the curated/general split — attributes with a shortcut versus those without — remains on the surface. The split disappeared only for the element vocabulary. Both are questions of mapping granularity, not questions of element write order.
- A name in the decoration chain can only be one `BlazorCodeFirst.Decorations` declares; no mechanism opens this position to the outside (decided 2026-08-14, #242). An extension method on `ElementView` declared outside the runtime is not a decoration, and BCF3026 names it as such. So a custom attribute set, HTMX's `hx-*` for instance, is spelled `.Attr("hx-get", url)`. An open form — a declaration that carries the attribute name in metadata, read by the generator and lowered into `.Attr` — was considered and rejected; `ARCHITECTURE.md` Appendix B.13 has the argument.
- No prefix-composing shortcut (`.Data(name, value)` / `.Aria(name, value)`) is provided; `data-*` and `aria-*` are written with `.Attr(name, value)` (decided 2026-08-14, #244). Every attribute shortcut on this surface copies the attribute name straight through, so the name appearing in the output also appears in the source; a composed name would break that traceability, and `.Attr("data-sku", …)` already has neither problem. `ARCHITECTURE.md` Appendix B.20 has the full argument.
- There are seven attribute shortcuts (`Href` / `Src` / `Alt` / `Id` / `Type` / `Title` / `Role`), and this set is closed (decided 2026-08-14, #321) — no new name is welcomed in. `.Class` does not belong to this set: its value is a sequence of classes, and it is the class channel's own spelling (`ARCHITECTURE.md` §2.7(A)). The seven share three traits that several other attributes also satisfy, so no rule decides where an eighth would stop; the boundary is history, not a rule, and the cost is limited since an attribute with no shortcut still emits through `.Attr(name, value)`. `ARCHITECTURE.md` Appendix B.21 has the full argument.
- `style` has no shortcut, and is written `.Attr("style", value)` (decided 2026-08-14, #321). `style` is the only other attribute on this surface, besides `class`, whose value is a sequence, and giving it a `class`-style channel was considered and rejected: either rule for folding it would duplicate or conflict with the class channel's own three rules. `ARCHITECTURE.md` Appendix B.22 has the full argument.

- The types accepted as an attribute value are exactly two: `string?` and `bool`. `bool` is Blazor's spelling for a conditional attribute: `true` becomes an attribute with an empty value, `false` omits the attribute entirely (`disabled` / `checked` / `hidden`). An unconditionally present attribute is written bare, the same as in HTML (`.Attr("disabled")`), and `bool` is spelled only for the conditional case (decided 2026-08-11, #178). The bare spelling is its own separate overload rather than a default argument on the `bool` overload, because a default argument would hit RS0027: an API with an optional argument must have the most arguments of its overload group, and this would tie with the `string?`-taking side. This carries a cost: a forgotten value (`.Attr("aria-label")`) becomes a value-less attribute rather than a compile error.
- `null` is an omission of the attribute itself (decided 2026-08-11, #171), measured to agree at every stage of the element path: the frame layer, static SSR, prerendering, the first interactive render, and re-renders in both directions. It is distinguished from `""` at every stage — `""` survives as `title=""`. This is a property of the element path; the component parameter path, conversely, stacks a frame even for `null`, so the same decision does not extend to `.Param`. Measurement was taken on the Server render mode only, relying on the fact that the frame omission happens in shared .NET code for WASM too, and that applying it to the DOM goes through the same `blazor.web.js` renderer on both Server and WASM.
- No `object?` is accepted (decided 2026-08-07, #158). What a non-string value's formatting follows is the culture of the thread that runs `RenderView`, and no code the author writes can decide that (measurement of when and where formatting happens is in `ARCHITECTURE.md`'s Appendix E.2) — so an `object` path's output would depend on ambient state invisible to the caller, and static folding (`ARCHITECTURE.md` §2.7(D)) could never apply in principle. `bool` has nothing to format, so it has neither problem. `int` / `DateTime` / an enum are stringified on the calling side (`.Attr("tabindex", index.ToString(CultureInfo.InvariantCulture))`), moving culture from an implicit ambient state to a visible, written choice. Reconsidering it needs a case caller-side stringification cannot carry. That Razor accepts `object` is not that case.
- The types accepted as a child are likewise limited to `string` and `View`, with no spelling for a number (decided 2026-08-14, #245). `Div[_n]` is CS1503 today; a number goes through an interpolated string or an explicit `ToString`. The previous item's grounds do not reach this channel, though: the formatting time and static-folding treatment agree between a numeric child and an interpolated string (measured, `ARCHITECTURE.md` Appendix E.2). Only one thing differs. An interpolation has somewhere to write how formatting happens (`$"{_n:F2}"`, `_n.ToString(CultureInfo.InvariantCulture)`), and `Div[_n]` has nowhere to add that — so what would be added is a second spelling, and its only difference from what can already be written is the absence of a place to write the culture. The grounds are not the formatting time but where formatting can be written. Oxpecker.ViewEngine's `IntNode` is a node for skipping escaping, but that is not a motivation here: escaping applies uniformly to a `Text` frame at render time, and there are not two paths for it. Measurement is pinned by `ChildValueSpellingTests` and `NonStringValueFormattingTests`. Reconsidering it needs a case interpolation and `ToString` cannot carry.
- There is exactly one name that accepts only `string` as a value: `class`. This name folds into the class channel, and because the channel concatenates decorations into one value as text, only text can be concatenated. The rule is written as this channel's own requirement, not as an enumeration of rejected overloads, so the same rule reaches unchanged even as `.Attr` gains more non-`string` overloads (#223). For `bool`, the meaning is not only undefined — the same spelling translates two different ways depending on how many class decorations the element carries, a translation break in the sense of the item below, arising here not from the HTML parser but from the generator's own folding. So `.Attr("class", bool)` is rejected by BCF3023, and a conditional class is written as a conditional expression on the string side (`.Class(active ? "on" : null)`). The value-less `.Attr("class")` reaches the same rule too: a bare spelling denotes presence, but a channel that concatenates as text has nothing to concatenate. What the two translations concretely become is in Appendix A's BCF3023.
- A term written `null` in a conditional class drops from the concatenation entirely (decided 2026-08-11, #236). `.Class("card").Class(on ? "active" : null)` is `class="card"` when `on` is false, with no leftover separating space. It is the same when the dropped term is the first one, and if every term drops, the attribute itself vanishes, the same as with a single decoration. Only `null` drops — `""` still takes part in the concatenation as a term, so the distinction above (`null` is the attribute's absence, `""` is a valueless attribute) is preserved inside the channel too. This rule is realized as a single `string.Concat`, and a spelling with only non-null terms allocates identically to when the concatenation operator was used (measured, #236).
- The tag name and an attribute name never become runtime values (decided 2026-08-14, #308 / #320). `Element(GetTagName())` stays BCF3009, and `.Attr($"data-{kind}", value)` stays BCF3011, because this surface reads a name to decide translation: the class channel and BCF3010's duplicate check both decide by name, and BCF3016's totality depends on the tag string being constant. One narrow exception exists on the receiving side of Blazor's `CaptureUnmatchedValues` (revised 2026-08-19, #387): `ElementView.Attrs(IReadOnlyDictionary<string, object>? value)` accepts a dictionary of already-resolved name/value pairs, since doing so introduces no new runtime-valued *name*, only values. `ARCHITECTURE.md` Appendix B.14 and its 2026-08-19 revision have the full argument, including what the `.Attrs` ordering costs.
- Three decorations are not attributes: `.Key` (Razor's `@key`), `.Ref` (`@ref`), and `.RenderMode` (`@rendermode`). None joins the attribute channel or the class channel — each falls to its own dedicated `RenderTreeBuilder` call (decided 2026-08-15, #309 / #310 / #311). The receiver splits by channel: `.Key` and `.Ref` are taken by both an element and a component, and `.RenderMode` only by a component. So these three cannot be declared entirely by `Decorations` — they also appear as members of `ComponentView<T>` itself; a separate type declares the same name the element side has, but splitting the receiver is preferred over splitting the spelling. In Razor these are all one family under the same `@` prefix, and splitting the name would break the mirror right there. Writing `.Key(null)` as a constant is the same decline as `ForEach(key: null)`, and emits no `SetKey` (§4.2).
- What `.Ref` takes is an Action, not an assignment target. Razor names a field with `@ref="_input"`, and the generated code assigns into it; this surface writes `.Ref(r => _input = r)`, and the author's own lambda performs the assignment. Given that `RenderTreeBuilder.AddElementReferenceCapture` takes `Action<ElementReference>`, there is no reason for the generator to construct an assignment. The requirement `.Bind`'s getter carries — that it must name a settable member (BCF3017 / BCF3018) — is not needed here, and the lambda is carried by the same rule as every other transplanted expression. The component side takes `Action<TComponent>`; the framework's own spelling is `Action<object>`, but `ComponentView<TComponent>` already knows the type, so there is no reason to make the author write the cast either.
- `.RenderMode` cannot be combined with the declaration form (BCF3034). Adding a call-site specification to a type that already carries the declaration-form attribute makes `ComponentFactory` throw at run time. The call-site form is still needed for the case where the same component is drawn interactively from one page and statically from another — that form carries no declaration form, so it is never a combination. Whether a type's declaration form is fixed is decided by looking up one attribute on the type symbol. Measured against the standard above — whether checking it would commit this repository to authoring and maintaining a table — there is no table to maintain here.
- The attribute for that declaration form is one the author declares themselves (investigated 2026-08-15, #311). The framework's own `Microsoft.AspNetCore.Components.RenderModeAttribute` is abstract, and not a single concrete derivation ships in net10.0's reference assemblies — Razor's `@rendermode` directive generates and attaches a derived class per component. So the shape a `.cs` author writes is the following. The predicate BCF3034 checks is "does it carry an attribute deriving `RenderModeAttribute`," and this catches both a hand-written declaration and inheritance through a base class equally.

```csharp
public sealed class InteractiveAttribute : RenderModeAttribute
{
    public override IComponentRenderMode Mode => RenderMode.InteractiveServer;
}
```


That there are five pieces of syntax with no mapping to an HTML element does not mean this surface added five vocabulary items on top of the lineage. Eleven HTML DSLs were surveyed (elm/html / Giraffe.ViewEngine / Falco.Markup / Feliz / Scalatags / kotlinx.html / maud / TSX / Plot / Lucid / TyXML). `Fragment` has a counterpart in all eleven (`frag`, `<>`, or a bare list), and so does `Raw` (`rawText`, `PreEscaped`, `Unsafe`). `Component<T>()` comes from Blazor owning the instance; what the lineage calls reuse — a function that returns markup — corresponds instead to `[ViewPart]`'s side (§4.3). `Slot` is HTML's own `<slot>`'s name, with a counterpart in both Razor's `ChildContent` and Oxpecker's block. Only two, `If` and `ForEach`, have no counterpart in the lineage (investigated 2026-08-09, #175).

Nine of the eleven can place their own `if` / `for` directly in a child position because they build the tree at run time. The tenth, maud, is a proc macro with its own grammar, not a library. Only the eleventh, TSX, sits on the same side as this surface: its child position is an expression, so it cannot place a statement, and writes `&&`, the ternary operator, and `.map` instead. `If` and `ForEach` occupy that position the same way, and the equivalent of `.map` is the collection-expression spread `[.. <source>.Select(…)]` (§4.2). Why this surface cannot place a statement follows §1.4: `Body` is an expression, and C# has no mechanism that converts a statement into a child-generating expression. Widening `Body` to a statement body, to give native `if` / `foreach` full static assignment, was considered and not adopted (#175); `ARCHITECTURE.md` Appendix B.23 has the argument.

Two-way binding (`.Bind`) decides names differently between an element and a component. On the element side, the author writes both the bound attribute name and the event name (`Input.Type("text").Bind("value", "oninput", () => _name)`). On the component side, the generator derives `ValueChanged` from the selector (`c => c.Value`), and derives `ValueExpression` too if the type declares it. This looks like an inconsistency, but it is the two sides of one rule — **only guess what can be verified**. On the component side, a guess can be confirmed by looking up `TComponent`'s type symbol: `ValueChanged` is required to exist and match in type, and becomes BCF3020 if it does not; `ValueExpression` is emitted only when declared with a matching type, and silently omitted otherwise (the same behavior as Razor — emitting it unconditionally for a type that does not declare it would fail the binding itself). Both can be confirmed, but only the former's confirmed result is used to reject. The element side has nothing to confirm a guess against, so it chooses not to guess, and takes both names from the author (the reason for rejecting a Razor-style element-derived name is in `ARCHITECTURE.md`'s Appendix B.16). One side of a mix-up needs no confirmation source and is checkable: an event name that does not start with `on` is stopped by BCF3019.

The types a value can bind are `string` and `bool`, plus **any type at all, when culture is written as a non-omittable argument** (decided 2026-08-14, #307): `Input.Type("number").Bind("value", "oninput", () => _age, CultureInfo.InvariantCulture)` can be written. This decision does not overturn #158 — #158's stated reason for this face was never the non-string type itself, it was **culture disappearing from the call site**. A non-omittable argument never reaches that reason: wrapping the attribute side in `BindConverter.FormatValue(value, culture:)` means, for any type, what enters the frame is a string already formatted under the written culture, and formatting finishes inside `RenderView` (measured, `ARCHITECTURE.md`'s Appendix E.2). So no basis for narrowing which type to admit, in #158's own terms, survives for any type.

This decision does not meet the stricter bar #158 placed on `.Attr`, though: a case caller-side stringification cannot carry. `s => { if (int.TryParse(s, out var v)) _n = v; }` can still be written today, same as ever. What it meets is the other bar written into this face — culture's visibility. These are two separate bars, and #158's side has not moved, which is why `.Attr` stays `string?` and `bool`. `.Attr` has no return path, and `.ToString(culture)` is a complete answer that loses nothing; `.Bind` has a return path, where the framework's own conversion table sits. What is added is not formatting — it is **the semantics of a conversion failure**.

When a parse fails, both the field and the DOM roll back to the previous value (Blazor's own specification — the field side measured, the DOM side by `bind-reject.spec.ts`). The rollback is already caused by `SetUpdatesAttributeName`, which is already emitted, and this decision adds not a single new emission. A consequence: binding a number to `oninput` runs a rollback on every keystroke, so a decimal point typed into an `int` never survives. Since the author writes the event name, whether to choose `onchange` instead is already in their hands. An empty string alone is not a rejection — Blazor reads it as that type's default value, so an empty `int` binding becomes `0` (both measured). If the value is optional, bind `int?` instead.

Culture is never guessed. Razor injects a culture chosen from the element's literal `type`, but this face does not read that literal — the same reason both names are taken from the author: `.Type(kind)` can be an expression. So the author must write `CultureInfo.InvariantCulture` for `type="number"` and `type="date"`, and forgetting it is not diagnosed. A check that fires only for a constant would catch the same mistake in some spellings and not others.

`format` can be written only for the four date/time types (`DateTime` / `DateTimeOffset` / `DateOnly` / `TimeOnly`) and their `Nullable<>` forms, because the framework declares a format-taking converter only for those eight types — this table is not authored here, but drawn from metadata. Writing it for any other type is BCF3031. This argument is needed for `<input type="date">` to work, since the browser requires `yyyy-MM-dd`, and this face cannot supply that from `type`.

On the component side, `TValue` carries no restriction and takes no culture, because the value passes into a parameter without ever going through the DOM, and no formatting intervenes.

The number of bindings on the same element is not limited to one. The motivating shape is a custom element: a Web Component with two or more two-way properties can have each one bound with `.Bind`, and ordinary diff detection draws it correctly. DOM resync, though, does not reach a custom element — the client returns only the form element's own `value` (or `checked` for a checkbox), and `null` for anything else, so there is nowhere for the repair to act on. Plain HTML has no element that carries two-way state across two attributes; `video` / `audio`'s `currentTime` / `volume` can be changed by the end user from native UI, but both are IDL properties that never reflect back to an attribute, and cannot be driven through the attribute path `.Bind` uses. That, though, is a fact about HTML, not a reason to reject this shape — this section's principle cuts both ways: for the same reason a shape that breaks nothing goes unchecked, a shape Blazor accepts and renders correctly is also never diagnosed. This once carried a diagnostic that rejected it, withdrawn after measurement refuted its basis (`ARCHITECTURE.md`'s Appendix B.5).

This principle's scope is the surface — how an element, an attribute, and a child are written. Single-direction data flow (BCF3001, §5.3) is not an exception to this principle; it sits on a different axis entirely. A shape where `Body` mutates state is not, to begin with, a shape "Blazor renders correctly" in the sense used here: Blazor does evaluate that expression and draw it, but the drawn result becomes a function of how many times `Body` was called, and that count is not part of Blazor's contract. A parent re-render, `StateHasChanged` from an unrelated handler on the same component, parameter assignment, and prerendering versus interactive rendering all change that count. So "it worked" here would only mean "it worked for the render count observed" — a shape that looks like what was written but produces a different result, the same kind of shape as the translation break this section is checking for.

Attributes are placed before children because that is how HTML writes them. This surface's own principle is to mirror the HTML vocabulary directly, and to layer nothing on top of it that would force relearning. The same principle is why none of the following is provided: a SwiftUI/Jetpack Compose-style layout container (`VStack` / `HStack` / `Grid`), a typed decoration (`.Padding()` / `.FontSize()`, and so on), or `Text()`. Since the output target is real HTML/CSS, a custom layout vocabulary would only stack a relearned vocabulary and implicit behavior on top of an already-complete lower layer. When elements need to sit side by side, express it as `Div.Class("row")[...]` plus external CSS (`.row { display: flex }`) — this never implicitly injects flex. Writing `.Attr("style", "display:flex")` explicitly, via the general-purpose `.Attr(name, value)`, is still possible; this sentence only states a layout recommendation and restricts no way of writing it. Why `style` has no shortcut is in the item above, and has nothing to do with CSS methodology.

### 4.2 Expressing lists and conditionals

A branch and a loop are written declaratively with the dedicated combinators `If` / `ForEach`.

```csharp
public partial class TaskListPage : BodyComponentBase
{
    private readonly List<TaskItem> _items = [];

    protected override View Body =>
        Div[
            Span["Tasks"],

            If(_items.Count == 0,
                then: () => Span.Class("empty")["No tasks yet"],
                otherwise: () => ForEach(_items,
                    key: t => t.Id,
                    content: item =>
                        Div.Class(item.Done ? "task done" : "task")[
                            Span[item.Title]]
                )
            ),

            Button.OnClick(AddItem)["Add Task"]];

    private void AddItem() => _items.Add(new TaskItem("New task"));
}
```

- `If` expands into a native `if` statement, and `ForEach` into `foreach` + `SetKey`. Each branch path is assigned a disjoint static sequence space, preventing state from carrying over incorrectly (`ARCHITECTURE.md` §2.4).
- `ForEach`'s `key` selector cannot be omitted. Either write one, or explicitly decline by writing `key: null` — there is no default, so declining is always visible at the call site. When written, the sequence number carries "syntactic position within the template" and the key carries "data identity," guaranteeing state survives a reorder, insertion, or deletion. Declined, that guarantee is gone, and the diff behaves like an index-derived key: a leading insertion rewrites every row and loses each row's local state (`ARCHITECTURE.md` §2.7(B)). A declined `ForEach` emits no `SetKey`, so `Fragment` / `Raw` / a bare `If` may sit at content's root.
- There are two spellings for building children from data, and the second is sugar for the first — they fold into the same node, agreeing down to not emitting `SetKey`.

  ```csharp
  Ul[ForEach(_columns, key: null, content: c => Li[c.Header])]
  Ul[[.. _columns.Select(c => Li[c.Header])]]
  ```

  A spread mixes with sibling children (`Ul[[Li["first"], .. proj, Li["last"]]]`). Only `<source>.Select(<inline expression lambda>)` folds; any other spread is BCF1003. The reason for this boundary is in `ARCHITECTURE.md`'s Appendix B.12.
- Using a native control construct directly inside `Body` (a block-bodied `if` / `foreach`, and so on) is also possible. The Source Generator transplants that syntax whole into the generated code and wraps it in a dynamic region (§5.3).

### 4.3 Splitting and reusing components

A piece of UI can be extracted into a static method carrying the `[ViewPart]` attribute. The Source Generator parses these too, and instead of calling them at run time, statically expands them at the call site.

```csharp
protected override View Body =>
    Div[
        AppHeader("My Application"),   // a [ViewPart] method, subject to static expansion
        BodyContent()];

[ViewPart]
private static View AppHeader(string title) =>
    Div.Class("app-header")[
        Span[title]];
```

A part that wraps content declares its return type as `SlotView`, and writes `Slot` in its body; the caller gives content through brackets (decided 2026-08-10, #176).

```csharp
protected override View Body =>
    Div[
        Card("Profile")[P["Body text"]],   // hand-written, wearing the same face as a built-in element
        Section.Class("body")[P["…"]]];

[ViewPart]
private static SlotView Card(string title) =>
    Div.Class("card")[
        H2[title],
        Slot];                             // the caller's content lands here
```

The reason brackets were chosen is not a spelling preference. All five surveyed HTML DSLs (Giraffe.ViewEngine / Falco.Markup / Feliz.ViewEngine / Oxpecker.ViewEngine / Razor) apply the same rule: a hand-written part takes children in the same shape a built-in element does. Falco states this explicitly, saying a component should preserve the same function *shape* as a standard element — `XmlAttribute list -> XmlNode list -> XmlNode`. The first three take positional arguments not because positional is correct, but because their built-in elements are positional-argument functions. Oxpecker is the one lineage whose child syntax is its own construct: its CE members are defined not on a concrete type but as a type extension of the `HtmlContainer` interface, so `{ }` attaches to a user function's return value too. Applying the same rule to this surface: since a built-in element's children go in `[…]`, a part's children go in `[…]` too.

An additional slot is an ordinary `View` parameter. As in `Panel(H2["Title"])[P["Body text"]]`, the named channel comes first and the primary content goes in brackets. This shape reuses exactly what this surface already carries in `Div.Class("card")[children]` and `Component<T>().Template(…)[…]`. Blazor's own framework carries the same asymmetry (the implicit `ChildContent` is plain markup, while an additional template is a child element named after its parameter), and so does Oxpecker. That the call site looks different for a single slot versus multiple slots is a shape this area has actually converged on, not an inconsistency.

Placing a named channel on the marker type (`Panel().Slot("header", H2["Title"])[P["Body text"]]`) was not adopted: it would introduce slot names as strings into a place where `Component<T>` uses typed selectors, and would additionally need three diagnostics — unknown name, duplicate name, missing — rebuilding, for a positional argument, what C#'s overload resolution already gives for free. Reconsidering it needs a case a positional argument cannot carry.

That `SlotView` has no conversion to `View` is what closes this surface's rule by type rather than by diagnostic. A forgotten bracket (`Div[Card("x")]`), a decoration (`Card("t").Class("x")`), and the positional-argument spelling #176 rejected (`Card("t", P["Body text"])`) are all rejected by C# first. The only diagnostic that needed to be newly introduced is BCF3025, which handles what the type system cannot see: a `Slot` written somewhere with no content to receive, and a part returning `SlotView` that writes `Slot` a number of times other than once. Giving `View` itself a bracket and unifying to one return type was not adopted, since it would shift this division of labor over to diagnostics instead (`ARCHITECTURE.md`'s Appendix B.9). A `View` parameter is an ordinary parameter, so it may be referenced any number of times; it neither captures nor shares, expanding the caller's subtree fresh at each reference, so an argument with a side effect runs once per reference (the same behavior as writing a `RenderFragment` twice).

When a method with no `[ViewPart]` returns `View`, the Source Generator cannot analyze its inside, and the method is treated as dynamic content evaluated at run time (the form where the returned `View` wraps a `RenderFragment`; §5.3).

Automatically treating any attribute-less static method that returns `View` as subject to expansion was not adopted (decided 2026-08-11, `ARCHITECTURE.md`'s Appendix B.11). The attribute is paid once per declaration, not per call site, and it declares "this declaration is meant to expand." Because that declaration exists, BCF1002 can report at the declaration site when a declaration fails to meet the expansion contract. Automating it would remove that declaration and force a choice between two options instead: silently dropping a non-conforming declaration to the dynamic path, or rejecting the unanalyzable-path spelling §5.3 deliberately preserves, spelling and all. The forgotten-attribute accident itself is real, and the answer is BCF3030 at the call site (#260): a `View` built from a forgotten declaration is empty at run time, and what drops is not performance but the output itself.

`[ViewPart]` cannot be declared as an extension member (decided 2026-08-09, #203). Both a classic `this` parameter (`static View Label(this string value)`) and a member inside a C# 14 `extension` block are BCF1002. The only call spelling this section gives is the plain call, `AppHeader("My Application")` — a trailing `.Foo(...)` chain is, per §4.1, reserved for decorating an element. The full reasoning behind this rejection is in `ARCHITECTURE.md`'s Appendix B.17.

Static expansion needs the declaration's source syntax, so it only works when the call site is in the same compilation as the declaration. A definition is collected from the current compilation's own syntax, and since IL carries no body syntax, a `[ViewPart]` in a referenced project or NuGet package becomes BCF1002 at the call site (`ARCHITECTURE.md`'s Appendix A). A part meant for reuse across an assembly boundary should be a component instead, used via `Component<T>()` (§6.2) or as a tag from `.razor` (§6.1).

Four transforms are central to this design: folding a decoration chain, `ForEach`'s key matching, `[ViewPart]`'s static inline expansion, and folding a static subtree. `ARCHITECTURE.md` §2.7 defines, for each, exactly which input turns into which generated code, as input/output examples.

---

## 5. Architecture and internal implementation

### 5.1 The compilation model: from Body to a rendering method

The Source Generator parses each component's `Body` (and any reachable `[ViewPart]` method) expression tree, and generates, into the same partial class, a rendering method with static sequence numbers embedded as constants.

Conceptual shape of the code generated from §4.1's `CounterPage`:

```csharp
// <auto-generated/> CounterPage.g.cs
public partial class CounterPage
{
    protected override void RenderView(RenderTreeBuilder __b)
    {
        __b.OpenElement(0, "div");                                    // Div + .Class
        __b.AddAttribute(1, "class", "counter");
        __b.OpenElement(2, "span");                                   // Span (mixed content)
        __b.AddContent(3, $"Count: {_count}");                        // a state reference, transplanted whole as syntax
        __b.CloseElement();
        __b.OpenElement(4, "button");                                 // Button + .OnClick
        __b.AddAttribute(5, "onclick",
            EventCallback.Factory.Create(this, () => _count++));      // a lambda, transplanted too
        __b.AddContent(6, "Increment");
        __b.CloseElement();
        __b.OpenElement(7, "button");
        __b.AddAttribute(8, "onclick",
            EventCallback.Factory.Create(this, () => _count = 0));
        __b.AddContent(9, "Reset");
        __b.CloseElement();
        __b.CloseElement();
    }
}
```

The connection to the base class takes the following shape.

```csharp
public abstract class BodyComponentBase : ComponentBase
{
    protected abstract View Body { get; }          // the design-time source of truth
    protected abstract void RenderView(RenderTreeBuilder builder);   // the SG generates the implementation

    protected sealed override void BuildRenderTree(RenderTreeBuilder builder)
        => RenderView(builder);
}
```

`Body` is never called at run time, not even once. A layout's `ChromeLayoutBase.Chrome` is a design-time getter of the same shape, and receives the same treatment. Every instance of the design-time API is an inert implementation that returns a default value — the design-time API here means every member of `Html` and `Decorations`, and every member of the design-time inert types `View` / `ComponentView<T>` / `ElementView`. Even if one were somehow evaluated, it would have no side effect, and the IL trimmer removes it in an AOT build. The design is verifiable by confirming, via `System.Reflection.Metadata`, that no MethodDef remains; the trim tests carry out that verification.

### 5.2 Statically settling the sequence number

Blazor's diff detection assumes a sequence number is settled statically at compile time. A runtime dynamic increment misleads the diffing algorithm on an element insertion or deletion, causing an unnecessary discard-and-regenerate of the subtree and losing component state.

This approach satisfies that assumption structurally: the Source Generator walks the expression tree depth-first, assigns each node a unique sequence range, and embeds it into the generated code as a constant. It does no more than what the Razor compiler already does for markup — done here for a C# expression instead. See `ARCHITECTURE.md` §2 for the formal definition of the assignment algorithm.

### 5.3 The statically analyzable subset (SSC) and the dynamic region

Static sequence assignment cannot hold for arbitrary C# code, so the scope of the analysis is explicitly defined.

SSC's interior holds two things, both subject to full static assignment: direct writing of an element helper/decoration/combinator inside a `Body` or `[ViewPart]` method, and a direct call to `Component<T>()`, `Fragment`, or `Raw` (including an inline lambda). Outside SSC, one of two treatments applies.

Transplantable syntax (a native `if` / `foreach` / `switch`, and so on) is transplanted whole into the generated code, and wrapped in a region (`OpenRegion` / `CloseRegion`) whose boundary carries a static sequence. Because a region isolates its sequence space, its internal dynamism never propagates out into the surrounding diffing.

An unanalyzable call is evaluated at run time, and the `RenderFragment` its returned `View` wraps is drawn inside a region. Only this path allocates on the heap normally. The only spelling that puts a fragment into a `View`, though, is an implicit conversion from `RenderFragment`, and a `View` built from the design-time API is empty at run time — so a `View`-returning method with no `[ViewPart]` is stopped by BCF3030, as long as its source declaration is in the current compilation. What remains on this path is a body that never uses the design-time API, and a call whose declaration cannot be read (`ARCHITECTURE.md`'s Appendix A, Appendix B.11).

In every case, correctness is preserved, and what is lost is only static optimization for that region. The generator notifies the loss of an optimization opportunity via the informational diagnostic BCF2001.

In the current implementation, the analyzer detects a state mutation inside a `Body` body (a direct write — assignment, compound assignment, increment/decrement — to an instance field/property) as the error diagnostic BCF3001. `Body` must be a pure state-to-UI projection, with state transitions left to event handlers. A `Button`'s onClick lambda (a deferred event handler), though, is excluded, since it runs only after rendering. Complete detection of an arbitrary unanalyzable path, such as a side effect reached through a method call, is not guaranteed. Applying this to a `[ViewPart]` body is a candidate for future extension, and is not part of this initial implementation's guaranteed scope.

That Razor carries no equivalent check is not a reason for this surface to lack this diagnostic. What sits on Razor's side is not a judgment but the absence of a check — the `Microsoft.AspNetCore.Components.Analyzers` Blazor bundles never looks at rendering purity. Other declarative UI facing the same problem leans toward checking it (Svelte 5 with a runtime error, Flutter with a debug assertion, the React Compiler with a lint). On top of that, the situation tilts worse on this surface: the generator does not copy `Body`, it transforms it — folding a static subtree into markup, statically assigning sequence numbers, isolating an exclusive branch into its own region (§5.1, §5.2, `ARCHITECTURE.md` §2.4–§2.7). These stay sound only as long as `Body` is a pure function of state, and nothing in this design guarantees that a `Body` carrying a side effect is invariant under these transforms. In Razor, the author can read the markup they themselves wrote; here, what actually runs is the generated `RenderView`, and the author never reads it. On a path that produces invisible output, a warning is a weaker guarantee than hand-written code.

The fence is deliberately narrow, though, and as stated above never reports a mutation that goes through a method call — this diagnostic stops accidents, and a deliberate side effect still passes if wrapped in one call (`Span[NextLabel()]`). This scope was chosen as calibration, not as proof of purity: the division of labor is that the diagnostic stops accidents, and intent takes the shape of a declaration.

### 5.4 Hot Reload strategy

Editing a `Body` expression appears as a change to the method body of the `RenderView` the Source Generator regenerates. A method-body swap is an edit class .NET Hot Reload (Edit and Continue) supports stably. Adding a `[ViewPart]` method is also "a member addition to an existing type," within the supported range. Blazor also already has a re-render-after-code-update path for Razor, via `MetadataUpdateHandler`. Because a BlazorCodeFirst component is an ordinary `ComponentBase`-derived type plus an ordinary generated method, it rides this existing path as-is — no dedicated reload mechanism is needed.

The behavior is specified as follows. An edit that inserts an element partway through `Body` reassigns the sequence numbers of the nodes that follow. On the first render immediately after a reload, that component's DOM subtree is rebuilt: the component's field state survives, while DOM-local state — in-progress focus, and so on — can be lost. This is the same semantics as editing a Razor file.

The one assumption this design depends on outside the Blazor standard is that a third-party Source Generator re-runs during an edit session and its updated generated code is carried through to EnC. This is tooling territory where behavior can differ across Visual Studio / `dotnet watch` / Rider, and needs confirmation per environment. A development-time fallback for a specific environment found not to carry a re-run through (a DEBUG-build-only interpreted mode) is recorded as an alternative in `ARCHITECTURE.md`'s Appendix C.

---

## 6. Two-way compatibility with the existing Blazor ecosystem

BlazorCodeFirst does not build its own closed world. An existing Razor component (`.razor`) or library (MudBlazor, QuickGrid, and so on) can be reused as-is.

### 6.1 Using BlazorCodeFirst inside Razor

A BlazorCodeFirst component is an ordinary Blazor component. Apart from its base type being `BodyComponentBase` and its rendering method being generated by the Source Generator, it looks to Blazor exactly like a hand-written C# component. So it can be named as a tag from an existing `.razor` file, the ordinary way.

```razor
@* ExistingPage.razor *@
<div class="legacy-layout">
    <StatusBadge Status="@currentStatus" />
</div>
```

```csharp
public partial class StatusBadge : BodyComponentBase
{
    [Parameter] public Status Status { get; set; } = default!;

    protected override View Body =>
        Span.Class(Status.IsHealthy ? "badge badge-ok" : "badge badge-alert")[Status.Label];
}
```

This direction carries no same-project restriction like §6.2's BCF3012, because what Razor resolves is a class name, and that class declaration is source the author wrote. The Source Generator only adds `RenderView`'s body, which Razor never needs to see. This is where it is asymmetric with §6.2's direction, which takes the generated output itself as a type argument.

`[ViewPart]` cannot be the entry point for this direction: as §4.3 states, static expansion requires the call site to be in the same compilation as the declaration, so no substance is ever generated that `.razor` could call. A piece meant to be shown to Razor should be a component instead (`ARCHITECTURE.md`'s Appendix B.4).

### 6.2 Using an existing Razor component inside BlazorCodeFirst

`Component<T>()` embeds an existing Blazor component, third-party ones included, into a code-first tree. Parameters bind through a static setter the Source Generator generates (no runtime compilation of an expression tree), so this is safe in an AOT environment too.

The type argument falls, as a literal, into the generated code's `OpenComponent<T>`, so it must be a type resolvable at generator run time. Because source generators cannot see each other's output, a `.razor` component declared in the same project cannot be specified as a type argument, and becomes BCF3012. Placing the same component in a referenced project or a NuGet package resolves it normally, and a hand-written C# component is always available.

```csharp
protected override View Body =>
    Div[
        Span["Data Grid"],
        Component<MudDataGrid<Order>>()
            .Param(g => g.Items, _orders)
            .Param(g => g.Dense, true)];
```

Naming a parameter through a selector stays as-is (decided 2026-08-11, #170). The selector does two jobs at once: the generator reads the parameter name `AddComponentParameter` requires from `g.Items`'s literal text, and C# itself already checks the value's type against the declared property type. Since `TValue` is inferred from both the selector and the value, `.Param(g => g.Dense, 42)` is already a compile error, before any BCF diagnostic even runs (measured). None of three shorter spellings — a string name, an object initializer, an initializer lambda — was adopted (`ARCHITECTURE.md`'s Appendix B.10). There is a separate way to remove this whole chain from the call site: `[ViewPart]` (§4.3). `Component<T>()` is not syntax for writing a hand-made part — it is the window for receiving a component Blazor itself owns as an instance. What the lineage calls a "component" — a function that returns markup — corresponds instead to `[ViewPart]`'s side.

Child content is given the same way as an element's, through `[...]`. `Component<T>()[children]` passes children into the `ChildContent` parameter, by the same rule Razor uses to bind nested content to `ChildContent`. A `RenderFragment` parameter other than `ChildContent` (`Footer`, for instance) is bound by naming it explicitly with `.Param(c => c.Footer, content)`. Naming `ChildContent` through `.Param` is fine too (the same as Razor's attribute form).

```csharp
protected override View Body =>
    Component<Card>()
        .Param(c => c.Title, "Title")
        .Param(c => c.Footer, Span["Footer"])[
            H2["Heading"],
            P["Body text"]];
```

It is BCF3013 when `T` has no `ChildContent` (a settable `[Parameter]` of type `RenderFragment` or `RenderFragment<TContext>`). When `ChildContent` is generic, the bracket binds together with an outer lambda that discards the context. The same bracket emits a different lambda depending on the target type, but since nothing inside the bracket names anything for reading the context, this reinterpretation is unambiguous. Giving the same parameter through both children and `.Param` or `.Template` is BCF3007.

A generic fragment named something other than `ChildContent` cannot be reached through the bracket, and is passed by naming it explicitly with `.Template`. `.Template` has two spellings: the context-ignoring side passes `View` as-is, and the generated side supplies the context-discarding outer lambda; the context-reading side names it with an inline expression lambda. When `ChildContent` is generic, the former and the bracket emit the same thing, so write the bracket, and reach for `.Template` only when reading the context. `EditForm.ChildContent` (`RenderFragment<EditContext>`) is the representative example.

```csharp
// ignoring context
Component<EditForm>()
    .Param(form => form.Model, _model)[
        Component<NameFields>().Param(fields => fields.Value, _model)]

// reading context
Component<EditForm>()
    .Param(form => form.Model, _model)
    .Template(form => form.ChildContent, editContext =>
        Fragment(
            Span[editContext.IsModified() ? "Modified" : "Unmodified"],
            Component<NameFields>().Param(fields => fields.Value, _model)))
```

For the second example's display to keep updating, the component that owns the form must itself construct the `EditContext` and re-render on `OnFieldChanged`. This is Blazor's own re-render propagation, not a limit on the context the template receives. The steps are on the documentation site (`/docs/components-and-reuse`).

The second argument must be an inline expression lambda; passing a method group or a delegate held in a variable is BCF3022, because what is transplanted into the generated code is the lambda body's syntax, and a delegate whose declaration is invisible has no syntax to transplant. `.Param` / `.Template` / a component's `.Bind` all follow the same three diagnostics (BCF3005 / BCF3006 / BCF3007) for how a target is selected and for the target parameter.

When a **value** of `RenderFragment<TContext>` is already in hand, pass it directly to the scalar `.Param` rather than `.Template`. Both reach the same parameter, but the delegate's identity differs: if `.Template`'s content reads state, the lambda carries a capture and becomes a new delegate on every render, and the receiving side redraws the child content as though the parameter had changed (a lambda that captures nothing is cached as a delegate by the C# compiler). If the caller instead holds one delegate and passes it via `.Param`, the reference stays stable and this redraw never happens. Which to choose depends on whether the author wants that identity.

---

## 7. Performance characteristics

The figures in §7.1 and §7.2 are measured values. The measurement procedure is recorded in `CONTRIBUTING.md` §Build and test, and every comparison mechanically verifies frame-sequence equivalence before measuring (it fails, producing no figure, if they are not equivalent). §7.3, and any spot in this chapter explicitly marked (predicted), are unmeasured.

### 7.1 Render cost and GC allocation

Because no runtime intermediate tree or builder object exists, this eliminates the extra GC load a code-first approach generally carries — it is zero. Per-operation allocation matched the equivalent Razor component's at two boundaries.

| Boundary | BlazorCodeFirst | Plain Razor |
| --- | --- | --- |
| `BuildRenderTree` alone | 40 B | 40 B |
| One render cycle, including diff | 176 B | 176 B |

The 40 B is the interpolated string's allocation, identical between the two. The allocation for frame construction itself never shows up as a measurable amount, since the builder reuses its frame array. So "zero extra GC load" means zero relative to Razor, not zero bytes. Execution time was measured too, but is not published here, since its variance is large and machine-dependent.

"The same form as the Razor compiler's output" came to hold for a static subtree eligible for folding too, once static-subtree folding (`ARCHITECTURE.md` §2.7(D)) was implemented (the scope of what is eligible is the third point below). This approach also folds a run of consecutive siblings whose values are compile-time constants into a single `AddMarkupContent` frame, collapsing a fully static component to one frame. The measurement in the table above was itself taken on a pair that supplies every text node from an expression, where neither compiler folds anything (a non-constant value is never eligible for folding). That the folded frame sequence matches Razor's was mechanically verified on a separate, statically written pair: both emit a single markup frame, with no difference beyond the sequence number.

Three differences remain: two come from Razor's own source form, and one is a shortfall on this approach's side. Razor splits text containing interpolation across multiple frames, and emits a newline between tags as its own markup frame — neither of these two is a shortfall on this approach's side (in both comparisons above, the Razor-side fixture is spelled on one line precisely to exclude the latter from the comparison). The third is which tags are eligible for folding: this approach narrows the target with an allow-list (§2.7(D)), while Razor places no restriction as long as the markup is static. So `Pre["code"]` or `Element("marquee")["x"]` folds under Razor and stays an element frame plus a text frame under this approach — correctness is unchanged; this is a choice that forgoes an optimization opportunity, and is the intended current state.

There is also a case where the frame count is the same but the frame kind differs. Because a run that would absorb only a single frame is not folded (§2.7(D)), a single static text node under an unfoldable element stays a text frame. `Div.Class(_cls)["x"]` emits `AddContent(2, "x")`, while Razor's `<div class="@cls">x</div>` emits `AddMarkupContent` at the same position — both agree in sequence number and frame count, so the equivalence check above only catches this difference once it compares frame kind too.

The dynamic-content path's (§5.3, the Opaque path) added cost is identical to hand-writing a `RenderFragment`. What was compared: a component that adds one child to an SSC-built element — a `View` a method with no `[ViewPart]` returns, internally a hand-written `RenderFragment` — against a Razor component that embeds the same `RenderFragment` directly. They matched at the same two boundaries as the table above.

| Boundary | BlazorCodeFirst (Opaque path) | Hand-written `RenderFragment` |
| --- | --- | --- |
| `BuildRenderTree` alone | 104 B | 104 B |
| One render cycle, including diff | 176 B | 176 B |

The generated code emits `AddContent(seq, ViewRuntime.FragmentOf(<call expression>))` (`ARCHITECTURE.md` §3.2), and the Razor side being compared embeds the same `RenderFragment` directly via `@MethodCall()`. Both end up falling to Blazor's own `RenderTreeBuilder.AddContent(int, RenderFragment?)`, and because this call opens a region for the fragment internally, the runtime frame sequence agrees even though the generated code writes no region of its own. `BuildRenderTree` alone's 104 B is the allocation for the interpolated string plus one delegate that binds the fragment, identical between the two. The `View`/`ViewRuntime.FragmentOf` indirection itself adds no bytes.

### 7.2 Diff-detection performance

Static sequence assignment keeps diffing's computational complexity at its theoretical minimum, O(|r_t| + |r_{t+1}|) (`ARCHITECTURE.md` §1.2). This was measured against an N-row keyed list, in the scenario of inserting a conditional element ahead of existing sibling elements (the condition under which the correspondence between `π` and generation order breaks down, §1.2).

| Approach | Sequence | Region | Key | Edit count | Rows discarded |
| --- | --- | --- | --- | --- | --- |
| This approach (generated code) | Static literal | Yes | Yes | **3** (independent of N) | **0** |
| Dynamic increment | Runtime counter | No | No | 3N+3 | N or 1 |
| Dynamic increment | Runtime counter | No | Yes | **3** (independent of N) | **0** |
| Dynamic increment | Runtime counter | Yes | Yes/No | 3N+3 | N |

At N = 10 / 100 / 1000, this approach was always 3 edits, confirming the complexity claim.

This difference was originally explained as "because the sequence number is fixed to syntactic position," but measurement does not support that. The dynamic-increment approach, with a key attached, reaches the same result as this approach (3 edits, 0 discarded) without using any static assignment at all. What the table above measured was exactly this keyed territory — a list with a key written on its `ForEach` lives there. In this scenario, where this approach shows an advantage over the dynamic-increment approach is against the unkeyed case: a list that declined by writing `key: null` sits on that side (§4.2), and it is the cost of declining that is measured here.

Where static assignment actually matters is when a region is involved. The table's bottom row matches this approach's own frame sequence exactly (mechanically verified to differ only in sequence number). If `OpenRegion`'s own sequence drifts from a runtime counter, the whole region containing the row is discarded, and the key is invalidated — key matching happens within a sibling group, and it is that group itself that moves. In this design, where `If` / `ForEach` emit a region (§5.3), the region frame's sequence being fixed to syntactic position is essential in exactly this sense. No real library emits a region with runtime numbering, by the way, so this configuration is a contrast constructed to isolate attribution, and is not a comparison target §7 publishes.

The number of rows discarded in the unkeyed case depends on a structural condition. When the number of sequences the banner consumes is a multiple of a row's frame width, row `i` matches row `i+1`'s position, and the diff rewrites text without discarding the component (only the trailing one row is discarded). When it is not a multiple, all N rows are discarded. Either way, the edit count is 3N+3, and the complexity claim is unaffected.

### 7.3 Wasm binary size

Because every mechanism, including parameter binding, is reflection-free (zero runtime dependency on `System.Reflection` / `System.Linq.Expressions`), the IL trimmer can remove unused code. With `TrimMode=full` and `ILLinkTreatWarningsAsErrors=true` enabled, the design removes the design-time getters' (`Body` / `Chrome`) and any unreachable design-time API's MethodDef at the metadata level. Compared with an equivalent library that uses reflection-based binding, this is projected to cut the AOT-compiled Wasm payload by roughly 20-30% (predicted). Against a plain Razor configuration, it is nearly identical.

---

## 8. Comparison with related projects

This library is not the first attempt at code-first UI in C#. Their target platforms differ, though, so none is a direct competitor — this chapter contrasts design approaches.

|                  | BlazorCodeFirst           | Comet                                           | Avalonia.Markup.Declarative               | CommunityToolkit.Maui.Markup     | Hand-written RenderTreeBuilder |
| ---------------- | ----------------------- | ----------------------------------------------- | ----------------------------------------- | -------------------------------- | -------------------------- |
| Render target   | Real DOM (Blazor)           | Native (MAUI handlers)                        | Native + browser (canvas rendering via Skia) | Native (MAUI). No browser support | Real DOM                      |
| Project status | This proposal                  | Archived July 2025 (proof of concept, no official support) | Active                                      | Active                             | Blazor standard (hand-writing not recommended) |
| UI model         | Declarative (re-evaluation + diff detection) | Declarative (runtime evaluation + reflection binding)       | Sugar over retained-mode construction                   | Sugar over retained-mode construction          | Declarative (fully manual)             |
| Sequence number   | Settled at compile time        | N/A (outside Blazor)                                | N/A                                    | N/A                           | Manually managed (fragile)     |
| State description       | A plain C# field        | A `State<T>` wrapper, switched with a reactive lambda        | ViewModel / `StateHasChanged`             | A binding expression                 | A plain field             |
| Runtime intermediate representation | None (SSC path)           | A UI tree + reflection                         | Holds a control instance                    | Holds a control instance          | None                       |
| AOT/trimming   | Fully compatible                | Depends on reflection                              | Compatible                                      | Compatible                             | Compatible                      |

Being DOM-native is the largest difference. Avalonia runs in a browser too, but that is canvas rendering via Skia, with no DOM — SEO, the accessibility tree, the CSS ecosystem, and SSR/prerendering do not apply to canvas rendering. BlazorCodeFirst projects declarative UI onto the real DOM/HTML, so it can use these Web-standard assets as-is.

Structural agreement with Blazor's diff detection is also a challenge unique to this library. A retained-mode family (Avalonia.Markup.Declarative, MAUI.Markup) holds and mutates a control instance, so the sequence-number problem never even arises for them. Attempting code-first on top of Blazor, on the other hand, makes this problem unavoidable, and this library resolves it with an approach of the same shape as the Razor compiler's. For hand-written `RenderTreeBuilder` or a runtime-tree approach, this problem becomes a source of breakdown on both correctness and performance.

And declarative semantics coexist with zero intermediate representation. The declarative feel of re-evaluating the whole UI on every render carries a standing cost — GC pressure — under a runtime tree-construction approach (the type Comet takes). Under a compile-time generation approach, the same feel is available with no runtime intermediate object.

---

## 9. Scope boundary

This design covers only the range directly connected to this library's core thread — compile-time static sequence assignment. Peripheral ideas such as design-tool integration or a CSS-framework adapter are considerations for a separate product independent of the core design, and are out of scope.

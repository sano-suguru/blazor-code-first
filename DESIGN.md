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
- The naming rule is to capitalize only the tag name's first letter, and reformat nothing further: `Figcaption`, not `FigCaption`; likewise `Colgroup` / `Optgroup` / `Datalist` / `Textarea` / `Blockquote` / `Fieldset` / `Hgroup`. A conforming HTML tag name is all lowercase with no separators, so this rule holds without exception across every curated helper — and it holds because group 5 was excluded: SVG and MathML element names are not all lowercase, and including them would break both the rule and the test invariants built on top of it. So the correspondence between helper name and tag name is a bijection; the inverse map is lowercasing the first letter, with no per-tag mapping to memorize (`KnownSymbolsSyncTests` pins the inverse side). The option of making the helper names all lowercase, matching the tag letter for letter, was rejected on top of a measurement that found zero collisions with C#'s reserved words (`ARCHITECTURE.md` 付録B.15).
- An attribute and an event are given not alongside the children but through a decoration chain directly after the tag (`Div.Class("card")["text"]`). Children are passed in the following `[...]`, mixing strings and `View`s; a plain string becomes a text node via implicit conversion, so there is no dedicated `Text()` syntax. A Blazor `RenderFragment` becomes a child as-is too. This positioning has a consequence beyond mere spelling preference (investigated 2026-08-09, #175): Lucid / Giraffe.ViewEngine / Falco.Markup place attributes in the same value namespace as children, and so respell attribute identifiers with a `_` prefix or suffix (`class_` / `_class_`). Placing attributes after the `.` is what lets `using static Html` work with no respelling of names — this backs up the placement #73 and #87 settled.
- `If` / `ForEach` (§4.2), `Component<T>()` (§6.2), `Fragment`, and `Raw` are syntax with no mapping to an HTML element. `Fragment` is wrapperless grouping; `Raw` injects a trusted HTML string directly into `RenderTreeBuilder.AddMarkupContent`. `Raw`'s trust boundary is the same as `MarkupString`'s, and passing an untrusted string through it becomes an XSS vector. Neither opens a single element frame, so neither can take a key or be decorated. A content-taking `[ViewPart]` call's (§4.3) `SlotView` return cannot be decorated for the same reason — but this is closed by the type, not a diagnostic: since every decoration is an extension method on `ElementView`, `Card("t").Class("x")` is CS1929, just like `Div["x"].Class("y")`.
- A layout inherits `ChromeLayoutBase`, and writes the design-time expression the layout itself draws in `Chrome`. The `Body` parameter Blazor requires of a layout (`LayoutComponentBase.Body`) is `RenderFragment?`, which converts implicitly to `View`, so it can be placed directly as an element's child, as in `Main[Body]`.
- Where this design positions type safety is not kotlinx.html's stance — a per-element type, content model, and attribute-applicability check enforced at compile time — but hiccup's / ScalaTags's, which take a unified node and a string tag. So the "type safety" this approach claims is at the C# level (the whole of `Body` is a typed C# expression, and composition and refactoring propagate through the type), and does not include an HTML-validity-level check. `Div.Href("/x")` / `Div.Attr("href", "/x")` / `Span[Div["x"]]` all emit exactly what was written, with no diagnostic.
- What is checked is not validity but a translation break. A shape where a design-time tree, serialized and parsed back, does not return to itself produces a different DOM under prerendering than under interactive rendering. The boundary is drawn at whether checking it would commit this repository to authoring and maintaining a table the check depends on (revised 2026-08-11, #155). It is checked when doing so is only copying a table the standard or the framework already ships as canonical; it is not checked when doing so would newly create an enumeration someone has to keep interpreting and adding to. For elements, the line this standard draws lines up exactly with whether the judgment is decided by a unary predicate over the element tag, or requires the binary relationship (parent, child). The unary side — whether an element can take children — is part of the content model, but is finite, stable, and mechanically derivable from the standard's own index. The binary side brings in interpretation at a scale quadratic in the number of elements, and mixes with the parser's normalization of otherwise-correct code, so that table can only be authored here. The term count is not the standard itself — it is the reason the standard draws its line there. This is the same logic behind defining the curated element set by rule rather than by enumeration.
- The unary side's first case is giving a void element children (BCF3016). The target is the 13 elements the HTML Living Standard lists as void elements, defined by rule the same way as the curated set; this document carries no enumeration of them (the list and firing conditions are in Appendix A A.1). The canonical table lives in `KnownSymbols.VoidTags`, and `KnownSymbolsSyncTests` pins its agreement with the curated and exclusion tables. Both a curated helper and `Element` fall to a single tag string before consulting the same table, so the two paths agree structurally, not by coincidence. Measured 2026-08-03 (net10.0 / ASP.NET Core 10.0.10): for `Img["child"]`, static SSR emits `<img src="/a.png">child</img>`. The HTML parser pushes the child out to a sibling text node, so under prerendering the child appears outside the element. Interactive rendering attaches a text node as `<img>`'s child for the same expression. `Br["child"]` gets its stray `</br>` reinterpreted as an opening tag, so `<br>` goes from one to two. `base` / `link` / `meta`, from exclusion group 1, are also void, so giving them children via `Element` falls under this check — group 1 was excluded because it is silently inert when written *without* children, a separate matter from being given children.
- The same standard reaches beyond elements. A mismatch between an event name and its handler's argument type is exactly that, and is checked as BCF3028 (decided 2026-08-11, #155). `.On("onclick", (KeyboardEventArgs e) => …)` compiles; the name and type stay mismatched, and it breaks at run time. Razor rejects the same shape, because it carries a correspondence table, `[EventHandler("onclick", typeof(MouseEventArgs), …)]`. This table is metadata the framework already ships as canonical, and reading it adds not a single enumeration for anyone to keep maintaining — so it sits inside the standard above, and there is no reason this surface should fall short of the check Razor performs. The test is assignability, not equality: `.On("onclick", (EventArgs e) => …)` stays valid. An event with no correspondence is not reported. Only the framework's correspondence table becomes unreadable in a compilation that does not reference `Components.Web`; a registration the author wrote themselves is still read there too (the two firing shapes and their sources are in Appendix A A.1).
- Measurement has found members on the unary side beyond void, and none of them are checked either. The binary side is not checked at all: it would commit this repository to authoring and maintaining a (parent, child) content-model table, and besides, the binary side includes shapes that are not even a mistake in how they were written — not even something a diagnostic could fix (`Table[Tr[Td["x"]]]` is written correctly, yet the parser inserts `tbody`, so the two paths diverge anyway). This document carries no enumeration of these (the list of measured residue, and which side each item belongs to, is in `ARCHITECTURE.md`'s Appendix D). That list is a record of positions chosen, not a work list to fill in.
- Whether an attribute may be applied is not checked (decided 2026-08-14, #335). `Div.Href("/x")` and `Div.Attr("href", "/x")` write an attribute `div` does not take, and neither is reported. #129 once placed one of its reasons on "the attribute vocabulary is open, so `.Attr` cannot be covered," but that is wrong: an open vocabulary means an unknown name cannot be judged — it does not mean a known name cannot be. BCF3009 already requires the tag, and BCF3011 the `.Attr` name, to each be a non-empty compile-time constant, so both spellings fall to the same pair, `("div", "href")`, and one table can answer for both — the HTML Living Standard's *Index — Attributes* and its `Element(s)` column, a table the standard already ships as canonical, the same as the curated set and the void set. So the boundary above, about whether checking it would commit this repository to authoring a table, does not exclude this check — what excludes it is the condition that comes before that. Static SSR and interactive rendering both emit `<div href="/x">` for `Div.Href("/x")`; it matches what was written, and the translation is not broken. What this surface reports is a shape that fails to match what was written. Reporting a shape that matches what was written but is meaningless as HTML would mean introducing a second axis — validity — and that is exactly the kotlinx.html-style position this section stated it does not take. `ARCHITECTURE.md`'s 付録B.5, which withdrew BCF3021, followed the same logic: since 付録D already leaves measured breaks unchecked, there is no position from which to reject only a shape that breaks nothing. The question of severity sits downstream of this axis, so introducing no new axis means no occasion to choose Warning arises either.
- Every type on the design-time surface is a lightweight marker type (an empty `readonly struct`). An element name returns `ElementView`, `Component<T>()` returns `ComponentView<T>`, and a decoration takes `ElementView` and returns `ElementView`. A child-taking indexer and a combinator return `View`, and both `ElementView` / `ComponentView<T>` convert implicitly to `View`. This chain of types is what makes the attribute-then-child order a type-system requirement — the reverse order (`Div["text"].Class("card")`) does not work, since the decoration's receiver would be `View` (BCF3008).
- A state reference, an interpolated string, and an event lambda are transplanted into generated code whole, as syntax (within the same partial class, so access to a `private` member is preserved). During transplant, a resolved type name is normalized to its fully-qualified form starting with `global::`; an unresolved type name that depends on lexical context cannot be transplanted safely, and becomes BCF3015.
- Structural limits remain on the mirror. Each is a shape that has somewhere to map on the HTML side with no image on the surface — capitalization is not one of them (the bijection above). First, for the element vocabulary: the split HTML never had — curated tags versus everything else via `Element("…")` — has been removed, but the six excluded groups, each with a reason, remain, so the mirror is not total. Second, the attribute vocabulary is open to begin with (`data-*` / `aria-*` / custom attributes); with nothing to map as a closed set, `.Attr(name, value)` stays the general-purpose path on the attribute side, and the curated/general split — attributes with a shortcut versus those without — remains on the surface. The split disappeared only for the element vocabulary. Both are questions of mapping granularity, not questions of element write order.
- A name in the decoration chain can only be one `BlazorCodeFirst.Decorations` declares; no mechanism opens this position to the outside (decided 2026-08-14, #242). An extension method on `ElementView` declared outside the runtime is not a decoration, and BCF3026 names it as such. So a custom attribute set, HTMX's `hx-*` for instance, is spelled `.Attr("hx-get", url)`. An open form is not impossible in principle — a declaration that carries the attribute name in metadata (`[Decoration("hx-get")]`), read by the generator and lowered into `.Attr("hx-get", …)`, would work. It was not adopted, for three reasons, all in `ARCHITECTURE.md`'s 付録B.13: what it buys is only less repetition, its cost lands on the attribute channel, and the extension point becomes an enumeration with no rule. Reconsidering it needs a case `.Attr`'s repetition cannot carry — a case where distributing attribute sets as packages becomes a goal of this surface. That Oxpecker.ViewEngine ships HTMX / Alpine / ARIA as separate packages is not that case (付録B.13).
- No prefix-composing shortcut (`.Data(name, value)` / `.Aria(name, value)`) is provided; `data-*` and `aria-*` are written with `.Attr(name, value)` (decided 2026-08-14, #244). Every one of the seven shortcuts (`Href` / `Src` / `Alt` / `Id` / `Type` / `Title` / `Role`) copies the attribute name straight through, so the name appearing in the output also appears in the source. `.Data("sku", …)` would become the first member to break that: the completed name, `data-sku`, would never be written anywhere. That name would have to be traced from outside C#. `site/`'s `data-theme-toggle` appears, in the identical spelling, at three places — `.Attr` in `SiteNav.cs`, the browser-side JS, and a Playwright selector — and only searching connects the three. This option does not remove the curated/general split from the item above, either: because `data-*` and `aria-*` are both open namespaces, the curation #99 did for elements never applies in the first place, and this would become a shortcut for syntax rather than for a name. There is also already `Html.Data`, the curated helper for the `<data>` element; overload resolution would not split, but `Data.Data("kind", "price")` would become writable. `<data value>` is the element for a machine-readable value, and `data-*` is the attribute family that hangs off it, so a scene with both on the same element is not contrived. `.Attr("data-sku", …)` has neither problem. Reconsidering it needs measurement that `.Attr`'s spelling is an actual obstacle.
- There are seven attribute shortcuts (`Href` / `Src` / `Alt` / `Id` / `Type` / `Title` / `Role`), and this set is closed (decided 2026-08-14, #321) — no new name is welcomed in. The seven share three traits: each spells the attribute name it emits verbatim (the item above, #244), each value is a single opaque string, and each emits exactly one attribute frame. `.Class` does not belong to this set: its value is a sequence of classes, and it is the class channel's own spelling, which concatenates decorations into one frame (`ARCHITECTURE.md` §2.7(A); the generator reads it as a distinct kind too). These three are necessary conditions, though, not sufficient ones — `for` / `value` / `name` / `placeholder` / `lang` / `dir` / `tabindex` all satisfy them too. Frequency gives the candidates an order, but not where to cut, so no rule can be written to turn away an eighth. For the element vocabulary, a rule could be written, because the standard's own index ships a closed set, and writing the exclusion reasons as six groups automatically decides the rest. The attribute vocabulary is open (the second limit noted above), and there is no closed set to map into. What decides this set's boundary is not a rule but history, and the rule here is simply not moving that boundary. The cost is limited: an attribute with no shortcut still emits through the same frame via `.Attr(name, value)`, so no capability is lost. Adding one needs measurement that `.Attr`'s spelling is an actual obstacle.
- `style` has no shortcut, and is written `.Attr("style", value)` (decided 2026-08-14, #321). It fails the second of the three traits above: `style`'s value is a sequence of declarations, and there is exactly one other attribute on this surface whose value is a sequence — `class`, which does have a channel. So the rule given to `.Style` would have to be one of two: choose a rule that does not fold, and `.Style("color:red").Style(cond ? "display:none" : null)` becomes BCF3010 — under a CSS reading both would survive, and the same spelling on `.Class` concatenates instead, so the two sequence-valued attributes would answer the same spelling in opposite ways. Choose a rule that does fold, and a second channel appears, separator `;`. The class channel already carries three rules today: the concatenation generation rule drops a `null` term (#236, `ARCHITECTURE.md` 付録B.7), BCF3023 rejects `bool`, and BCF3024 watches for coexistence with `.Bind` — all written under the name `class`, and a second channel would duplicate all three, per name. Either rule buys only shorter spelling, and `.Attr("style", …)` still emits the same attribute frame regardless. So `style` is not folded, and carries no channel. Placing `.Attr("style", …)` twice on the same element is BCF3010, the same as every name other than `class`. Just as #244 placed `data-*` on `.Attr`, `style` too takes `.Attr`'s rule as-is, unchanged. Before this decision, the one sentence rejecting the layout vocabulary below also removed a `style` shortcut along with it — the reason written there was a recommendation of classes over external CSS, a preference about CSS methodology. An inline style is valid HTML, and Blazor draws it correctly. For the same reason a shape that breaks nothing goes unchecked, this surface does not decide the rights and wrongs of how something is written. The exclusion stands, but its grounds have moved here.

- The types accepted as an attribute value are exactly two: `string?` and `bool`. `bool` is Blazor's spelling for a conditional attribute: `true` becomes an attribute with an empty value, `false` omits the attribute entirely (`disabled` / `checked` / `hidden`). An unconditionally present attribute is written bare, the same as in HTML (`.Attr("disabled")`), and `bool` is spelled only for the conditional case (decided 2026-08-11, #178). The bare spelling is its own separate overload rather than a default argument on the `bool` overload, because a default argument would hit RS0027: an API with an optional argument must have the most arguments of its overload group, and this would tie with the `string?`-taking side. This carries a cost: a forgotten value (`.Attr("aria-label")`) becomes a value-less attribute rather than a compile error.
- `null` is an omission of the attribute itself (decided 2026-08-11, #171), measured to agree at every stage of the element path: the frame layer, static SSR, prerendering, the first interactive render, and re-renders in both directions. It is distinguished from `""` at every stage — `""` survives as `title=""`. This is a property of the element path; the component parameter path, conversely, stacks a frame even for `null`, so the same decision does not extend to `.Param`. Measurement was taken on the Server render mode only, relying on the fact that the frame omission happens in shared .NET code for WASM too, and that applying it to the DOM goes through the same `blazor.web.js` renderer on both Server and WASM.
- No `object?` is accepted (decided 2026-08-07, #158). What a non-string value's formatting follows is the culture of the thread that runs `RenderView`, and no code the author writes can decide that (measurement of when and where formatting happens is in `ARCHITECTURE.md`'s Appendix E.2) — so an `object` path's output would depend on ambient state invisible to the caller, and static folding (`ARCHITECTURE.md` §2.7(D)) could never apply in principle. `bool` has nothing to format, so it has neither problem. `int` / `DateTime` / an enum are stringified on the calling side (`.Attr("tabindex", index.ToString(CultureInfo.InvariantCulture))`), moving culture from an implicit ambient state to a visible, written choice. Reconsidering it needs a case caller-side stringification cannot carry. That Razor accepts `object` is not that case.
- The types accepted as a child are likewise limited to `string` and `View`, with no spelling for a number (decided 2026-08-14, #245). `Div[_n]` is CS1503 today; a number goes through an interpolated string or an explicit `ToString`. The previous item's grounds do not reach this channel, though: the formatting time and static-folding treatment agree between a numeric child and an interpolated string (measured, `ARCHITECTURE.md` Appendix E.2). Only one thing differs. An interpolation has somewhere to write how formatting happens (`$"{_n:F2}"`, `_n.ToString(CultureInfo.InvariantCulture)`), and `Div[_n]` has nowhere to add that — so what would be added is a second spelling, and its only difference from what can already be written is the absence of a place to write the culture. The grounds are not the formatting time but where formatting can be written. Oxpecker.ViewEngine's `IntNode` is a node for skipping escaping, but that is not a motivation here: escaping applies uniformly to a `Text` frame at render time, and there are not two paths for it. Measurement is pinned by `ChildValueSpellingTests` and `NonStringValueFormattingTests`. Reconsidering it needs a case interpolation and `ToString` cannot carry.
- There is exactly one name that accepts only `string` as a value: `class`. This name folds into the class channel, and because the channel concatenates decorations into one value as text, only text can be concatenated. The rule is written as this channel's own requirement, not as an enumeration of rejected overloads, so the same rule reaches unchanged even as `.Attr` gains more non-`string` overloads (#223). For `bool`, the meaning is not only undefined — the same spelling translates two different ways depending on how many class decorations the element carries, a translation break in the sense of the item below, arising here not from the HTML parser but from the generator's own folding. So `.Attr("class", bool)` is rejected by BCF3023, and a conditional class is written as a conditional expression on the string side (`.Class(active ? "on" : null)`). The value-less `.Attr("class")` reaches the same rule too: a bare spelling denotes presence, but a channel that concatenates as text has nothing to concatenate. What the two translations concretely become is in Appendix A's BCF3023.
- A term written `null` in a conditional class drops from the concatenation entirely (decided 2026-08-11, #236). `.Class("card").Class(on ? "active" : null)` is `class="card"` when `on` is false, with no leftover separating space. It is the same when the dropped term is the first one, and if every term drops, the attribute itself vanishes, the same as with a single decoration. Only `null` drops — `""` still takes part in the concatenation as a term, so the distinction above (`null` is the attribute's absence, `""` is a valueless attribute) is preserved inside the channel too. This rule is realized as a single `string.Concat`, and a spelling with only non-null terms allocates identically to when the concatenation operator was used (measured, #236).
- The tag name and an attribute name never become runtime values (decided 2026-08-14, #308 / #320). `Element(GetTagName())` stays BCF3009, and `.Attr($"data-{kind}", value)` stays BCF3011. Nor is a dictionary-spread that passes attributes en masse (Razor's `@attributes`) provided. The reason is that this surface reads a name to decide translation. The two items just above, on the class channel, decide by name whether a decoration joins it; BCF3010's duplicate check decides by name too. Once the name becomes a value, the former breaks and the latter falls silent: a runtime `class` cannot join folding, so it takes its own frame, and on an element with a spread Blazor resolves the duplicate last-wins — the folded value simply disappears there (measured, `AttributeSplatMeasurementTests`). A second rule, replacement, would sit next to the channel's own rule, concatenation, with a key nowhere written in the source deciding which one takes effect. What is lost on the tag side is BCF3016's totality: the agreement — that a curated helper and `Element` fall to a single tag string before consulting the same table — depends on that tag string being a constant. An author who writes this shape today gets a diagnostic right where they wrote it, and rewrites it to a constant spelling. Open the path, and what they receive instead is output that silently lost its `class`, or a check silently skipped (the full mechanism and cost this rejects is in `ARCHITECTURE.md`'s 付録B.14). Reconsidering it needs a case a constant tag and a constant attribute name cannot carry.
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

Nine of the eleven can place their own `if` / `for` directly in a child position because they build the tree at run time, and can therefore place a statement. The tenth, maud, has `@if` / `@for` / `@match` / `@let`, but it is a proc macro that parses its own grammar, not a library. Only the eleventh, TSX, sits on the same side as this surface: its child position is an expression, so it cannot place a statement, and writes `&&`, the ternary operator, and `.map` instead. The one precedent that shares this constraint answers it with the host language's own expression operators rather than a dedicated combinator. `If` and `ForEach` occupy that position, and the equivalent of `.map` is the collection-expression spread `[.. <source>.Select(…)]` (§4.2). Why this surface cannot place a statement follows §1.4: `Body` is an expression, and C# has no mechanism that converts a statement into a child-generating expression. Widening `Body` to a statement body, to give native `if` / `foreach` full static assignment, was considered on 2026-08-11 and not adopted (#175). As long as the channel for passing children is the bracket, making room for a statement would mean swapping the child channel for a lambda block, and that stops mirroring how HTML writes children inside a tag. And it does not remove the two pieces of syntax anyway — a plain `foreach` has nowhere to place a key, and a statement cannot be placed where an expression is required, so an approach meant to remove the two would add to them instead, without removing anything. Note that this is not §5.3's native control construct, which is a degradation to a dynamic region — it preserves correctness but gains no static assignment. What was considered and rejected here is an approach aimed at removing that degradation entirely. Reconsidering it needs measurement that these two are an actual learning obstacle.

Two-way binding (`.Bind`) decides names differently between an element and a component. On the element side, the author writes both the bound attribute name and the event name (`Input.Type("text").Bind("value", "oninput", () => _name)`). On the component side, the generator derives `ValueChanged` from the selector (`c => c.Value`), and derives `ValueExpression` too if the type declares it. This looks like an inconsistency, but it is the two sides of one rule — **only guess what can be verified**. On the component side, a guess can be confirmed by looking up `TComponent`'s type symbol: `ValueChanged` is required to exist and match in type, and becomes BCF3020 if it does not; `ValueExpression` is emitted only when declared with a matching type, and silently omitted otherwise (the same behavior as Razor — emitting it unconditionally for a type that does not declare it would fail the binding itself). Both can be confirmed, but only the former's confirmed result is used to reject. The element side has nothing to confirm a guess against, so it chooses not to guess, and takes both names from the author (the reason for rejecting a Razor-style element-derived name is in `ARCHITECTURE.md`'s 付録B.16). One side of a mix-up needs no confirmation source and is checkable: an event name that does not start with `on` is stopped by BCF3019.

The types a value can bind are `string` and `bool`, plus **any type at all, when culture is written as a non-omittable argument** (decided 2026-08-14, #307): `Input.Type("number").Bind("value", "oninput", () => _age, CultureInfo.InvariantCulture)` can be written. This decision does not overturn #158 — #158's stated reason for this face was never the non-string type itself, it was **culture disappearing from the call site**. A non-omittable argument never reaches that reason: wrapping the attribute side in `BindConverter.FormatValue(value, culture:)` means, for any type, what enters the frame is a string already formatted under the written culture, and formatting finishes inside `RenderView` (measured, `ARCHITECTURE.md`'s Appendix E.2). So no basis for narrowing which type to admit, in #158's own terms, survives for any type.

This decision does not meet the stricter bar #158 placed on `.Attr`, though: a case caller-side stringification cannot carry. `s => { if (int.TryParse(s, out var v)) _n = v; }` can still be written today, same as ever. What it meets is the other bar written into this face — culture's visibility. These are two separate bars, and #158's side has not moved, which is why `.Attr` stays `string?` and `bool`. `.Attr` has no return path, and `.ToString(culture)` is a complete answer that loses nothing; `.Bind` has a return path, where the framework's own conversion table sits. What is added is not formatting — it is **the semantics of a conversion failure**.

When a parse fails, both the field and the DOM roll back to the previous value (Blazor's own specification — the field side measured, the DOM side by `bind-reject.spec.ts`). The rollback is already caused by `SetUpdatesAttributeName`, which is already emitted, and this decision adds not a single new emission. A consequence: binding a number to `oninput` runs a rollback on every keystroke, so a decimal point typed into an `int` never survives. Since the author writes the event name, whether to choose `onchange` instead is already in their hands. An empty string alone is not a rejection — Blazor reads it as that type's default value, so an empty `int` binding becomes `0` (both measured). If the value is optional, bind `int?` instead.

Culture is never guessed. Razor injects a culture chosen from the element's literal `type`, but this face does not read that literal — the same reason both names are taken from the author: `.Type(kind)` can be an expression. So the author must write `CultureInfo.InvariantCulture` for `type="number"` and `type="date"`, and forgetting it is not diagnosed. A check that fires only for a constant would catch the same mistake in some spellings and not others.

`format` can be written only for the four date/time types (`DateTime` / `DateTimeOffset` / `DateOnly` / `TimeOnly`) and their `Nullable<>` forms, because the framework declares a format-taking converter only for those eight types — this table is not authored here, but drawn from metadata. Writing it for any other type is BCF3031. This argument is needed for `<input type="date">` to work, since the browser requires `yyyy-MM-dd`, and this face cannot supply that from `type`.

On the component side, `TValue` carries no restriction and takes no culture, because the value passes into a parameter without ever going through the DOM, and no formatting intervenes.

The number of bindings on the same element is not limited to one. The motivating shape is a custom element: a Web Component with two or more two-way properties can have each one bound with `.Bind`, and ordinary diff detection draws it correctly. DOM resync, though, does not reach a custom element — the client returns only the form element's own `value` (or `checked` for a checkbox), and `null` for anything else, so there is nowhere for the repair to act on. Plain HTML has no element that carries two-way state across two attributes; `video` / `audio`'s `currentTime` / `volume` can be changed by the end user from native UI, but both are IDL properties that never reflect back to an attribute, and cannot be driven through the attribute path `.Bind` uses. That, though, is a fact about HTML, not a reason to reject this shape — this section's principle cuts both ways: for the same reason a shape that breaks nothing goes unchecked, a shape Blazor accepts and renders correctly is also never diagnosed. This once carried a diagnostic that rejected it, withdrawn after measurement refuted its basis (`ARCHITECTURE.md`'s 付録B.5).

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

  A spread mixes with sibling children (`Ul[[Li["first"], .. proj, Li["last"]]]`). Only `<source>.Select(<inline expression lambda>)` folds; any other spread is BCF1003. The reason for this boundary is in `ARCHITECTURE.md`'s 付録B.12.
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

That `SlotView` has no conversion to `View` is what closes this surface's rule by type rather than by diagnostic. A forgotten bracket (`Div[Card("x")]`), a decoration (`Card("t").Class("x")`), and the positional-argument spelling #176 rejected (`Card("t", P["Body text"])`) are all rejected by C# first. The only diagnostic that needed to be newly introduced is BCF3025, which handles what the type system cannot see: a `Slot` written somewhere with no content to receive, and a part returning `SlotView` that writes `Slot` a number of times other than once. Giving `View` itself a bracket and unifying to one return type was not adopted, since it would shift this division of labor over to diagnostics instead (`ARCHITECTURE.md`'s 付録B.9). A `View` parameter is an ordinary parameter, so it may be referenced any number of times; it neither captures nor shares, expanding the caller's subtree fresh at each reference, so an argument with a side effect runs once per reference (the same behavior as writing a `RenderFragment` twice).

When a method with no `[ViewPart]` returns `View`, the Source Generator cannot analyze its inside, and the method is treated as dynamic content evaluated at run time (the form where the returned `View` wraps a `RenderFragment`; §5.3).

Automatically treating any attribute-less static method that returns `View` as subject to expansion was not adopted (decided 2026-08-11, `ARCHITECTURE.md`'s 付録B.11). The attribute is paid once per declaration, not per call site, and it declares "this declaration is meant to expand." Because that declaration exists, BCF1002 can report at the declaration site when a declaration fails to meet the expansion contract. Automating it would remove that declaration and force a choice between two options instead: silently dropping a non-conforming declaration to the dynamic path, or rejecting the unanalyzable-path spelling §5.3 deliberately preserves, spelling and all. The forgotten-attribute accident itself is real, and the answer is BCF3030 at the call site (#260): a `View` built from a forgotten declaration is empty at run time, and what drops is not performance but the output itself.

`[ViewPart]` cannot be declared as an extension member (decided 2026-08-09, #203). Both a classic `this` parameter (`static View Label(this string value)`) and a member inside a C# 14 `extension` block are BCF1002. The only call spelling this section gives is the plain call, `AppHeader("My Application")` — a trailing `.Foo(...)` chain is, per §4.1, reserved for decorating an element. The full reasoning behind this rejection is in `ARCHITECTURE.md`'s 付録B.17.

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

An unanalyzable call is evaluated at run time, and the `RenderFragment` its returned `View` wraps is drawn inside a region. Only this path allocates on the heap normally. The only spelling that puts a fragment into a `View`, though, is an implicit conversion from `RenderFragment`, and a `View` built from the design-time API is empty at run time — so a `View`-returning method with no `[ViewPart]` is stopped by BCF3030, as long as its source declaration is in the current compilation. What remains on this path is a body that never uses the design-time API, and a call whose declaration cannot be read (`ARCHITECTURE.md`'s Appendix A, 付録B.11).

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

BlazorCodeFirstのコンポーネントは通常のBlazorコンポーネントです。基底型が `BodyComponentBase` であることと、レンダリングメソッドをSource Generatorが生成することを除けば、Blazorから見た姿は手書きのC#コンポーネントと変わりません。したがって既存の `.razor` からは、通常どおりタグとして名指せます。

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

この方向には§6.2のBCF3012のような同一プロジェクト制限がありません。Razorが解決するのはクラス名であり、そのクラス宣言は作者が書いたソースだからです。Source Generatorが足すのは `RenderView` の本体だけで、Razor側はそれを見る必要がありません。生成物そのものを型引数に取る§6.2の方向とは、ここが非対称です。

`[ViewPart]` はこの方向の入口にはなりません。§4.3のとおり静的展開は呼び出しサイトが宣言と同一のコンパイル内にあることを要求し、`.razor` から呼べる実体は生成されないためです。Razorへ見せたい部分はコンポーネントにします(`ARCHITECTURE.md` 付録B.4)。

### 6.2 BlazorCodeFirstの中で既存のRazorコンポーネントを使う

`Component<T>()` で、サードパーティ製を含む既存のBlazorコンポーネントをコードファーストツリーへ組み込めます。パラメータはSource Generatorが生成する静的セッターでバインドされるため(式木のランタイムコンパイルなし)、AOT環境でも安全です。

型引数は生成コード中の `OpenComponent<T>` へリテラルとして落ちるため、ジェネレータ実行時に解決できる型でなければなりません。ソースジェネレータは互いの出力が見えないため、同一プロジェクト内で宣言された `.razor` コンポーネントは型引数に指定できず、BCF3012となります。同じコンポーネントを参照先プロジェクトやNuGetパッケージに置けば通常どおり解決し、手書きのC#コンポーネントも常に利用できます。

```csharp
protected override View Body =>
    Div[
        Span["Data Grid"],
        Component<MudDataGrid<Order>>()
            .Param(g => g.Items, _orders)
            .Param(g => g.Dense, true)];
```

パラメータをセレクタで名指す形は据え置きます(2026-08-11決定、#170)。セレクタは2つ仕事をしています。生成器は `AddComponentParameter` が要求するパラメータ名を `g.Items` の字面から読み、値の型はC#自身が宣言済みのプロパティ型と照合します。`TValue` がセレクタと値の双方から推論されるため、`.Param(g => g.Dense, 42)` はBCFの診断より先にコンパイルエラーです(実測)。より短い3つの綴り ── 文字列名、オブジェクト初期化子、初期化ラムダ ── はいずれも採りませんでした(`ARCHITECTURE.md` 付録B.10)。呼び出しサイトからこのチェーンごと消す手段は別にあり、それが `[ViewPart]` です(§4.3)。`Component<T>()` は自作の部品を書くための構文ではなく、Blazorが実体を所有するコンポーネントを受け取る窓口です。系譜が「コンポーネント」と呼ぶもの、すなわちマークアップを返す関数に当たるのは `[ViewPart]` の側です。

子コンテンツは要素と同じく `[...]` で与えます。`Component<T>()[children]` は Razor が入れ子コンテンツを
`ChildContent` に束縛するのと同じ規則で、children を `ChildContent` パラメータへ渡します。
`ChildContent` 以外の `RenderFragment` パラメータ(`Footer` 等)は `.Param(c => c.Footer, content)`
で名前を指して束縛します。`ChildContent` を `.Param` で名指してもかまいません(Razor の属性形と同じ)。

```csharp
protected override View Body =>
    Component<Card>()
        .Param(c => c.Title, "タイトル")
        .Param(c => c.Footer, Span["脚注"])[
            H2["見出し"],
            P["本文"]];
```

`T` が `ChildContent`(settable な `[Parameter]`、型は `RenderFragment` または
`RenderFragment<TContext>`)を持たない場合は BCF3013 です。`ChildContent` がジェネリックなら、角括弧は
コンテキストを捨てる外側のラムダを伴って束縛します。同じ角括弧が対象の型によって違うラムダを発行しますが、
角括弧の中にはコンテキストを読むための名前が無いため、この読み替えは一意です。同じパラメータを children と
`.Param` または `.Template` の両方で与えると BCF3007 です。

`ChildContent` 以外の名前を持つジェネリックなフラグメントは、括弧では届かず `.Template` で名前を指して
渡します。`.Template` の綴りは2つあり、コンテキストを使わない側は `View` をそのまま渡して、コンテキストを
捨てる外側のラムダを生成側が補います。コンテキストを読む側はインラインの式ラムダで名前を与えます。
`ChildContent` がジェネリックな場合、前者と角括弧は同じものを発行するため角括弧で書き、コンテキストを
読むときだけ後者を使います。`EditForm.ChildContent`(`RenderFragment<EditContext>`)がその代表例です。

```csharp
// コンテキストを使わない
Component<EditForm>()
    .Param(form => form.Model, _model)[
        Component<NameFields>().Param(fields => fields.Value, _model)]

// コンテキストを読む
Component<EditForm>()
    .Param(form => form.Model, _model)
    .Template(form => form.ChildContent, editContext =>
        Fragment(
            Span[editContext.IsModified() ? "変更あり" : "変更なし"],
            Component<NameFields>().Param(fields => fields.Value, _model)))
```

2つ目の例の表示が変化し続けるには、フォームを抱えるコンポーネント自身が `EditContext` を構築して
`OnFieldChanged` で再描画する必要があります。これはBlazorの再描画の伝播であって、テンプレートが受け取る
コンテキストの制限ではありません。手順はドキュメントサイト(`/docs/components-and-reuse`)にあります。

第2引数はインラインの式ラムダでなければならず、メソッドグループや変数に入れたデリゲートを渡すと BCF3022 です。
生成コードへ移植するのはラムダ本体の構文であり、宣言の見えないデリゲートには移植すべき構文が無いためです。
`.Param` / `.Template` / コンポーネントの `.Bind` は、対象の選び方と対象パラメータについて同じ3つの診断
(BCF3005 / BCF3006 / BCF3007)に従います。

すでに `RenderFragment<TContext>` の**値**を持っている場合は、`.Template` ではなくスカラーの `.Param` へ
そのまま渡します。両者は同じパラメータへ届きますが、デリゲートの同一性が異なります。`.Template` の内容が
状態を読めばラムダは捕捉を持ち、レンダーごとに新しいデリゲートになります。受け取り側はパラメータが変化した
ものとして子コンテンツを描き直します(何も捕捉しないラムダはC#コンパイラがデリゲートをキャッシュします)。
呼び出し側が1つのデリゲートを保持して `.Param` で渡せば参照が安定し、この描き直しは起きません。どちらを
選ぶかは、この同一性を作者が持ちたいかどうかで決まります。

---

## 7. パフォーマンス特性

§7.1と§7.2の数値は実測値です。測定手順は `CONTRIBUTING.md` §Build and test に記載し、いずれの比較も測定前にフレーム列の等価性を機械的に検証します(等価でなければ数値を出さずに失敗します)。§7.3および本章で明示的に(予測値)と記した箇所は未実測です。

### 7.1 レンダリングコストとGCアロケーション

実行時の中間ツリーやビルダーオブジェクトは存在しないため、コードファースト方式に一般的に伴う追加のGC負荷はゼロです。等価なRazorコンポーネントとの1オペレーションあたりアロケーションは2つの境界で一致しました。

| 境界 | BlazorCodeFirst | 素のRazor |
| --- | --- | --- |
| `BuildRenderTree` 単体 | 40 B | 40 B |
| Diffを含む1レンダリングサイクル | 176 B | 176 B |

40 Bは補間文字列の割り当てであり両者で同一です。フレーム構築自体の割り当ては測定可能な量として現れません(ビルダーがフレーム配列を再利用するため)。すなわち「追加のGC負荷はゼロ」とはRazor比でゼロという意味であり、バイト数がゼロという意味ではありません。実行時間は測定していますが、分散が大きく機械依存であるため本書には掲載しません。

「Razorコンパイラの出力と同形式」は、静的サブツリーの畳み込み(`ARCHITECTURE.md` §2.7(D))を実装した時点で、畳み込み対象となる静的サブツリーについても成り立つようになりました(対象の範囲は後述の3点目)。本方式も、値がコンパイル時定数である連続した兄弟を単一の `AddMarkupContent` フレームへ畳み込み、完全に静的なコンポーネントはフレーム1つに落ちます。上表の測定自体は全テキストノードを式から供給した対で行っており、この対ではどちらのコンパイラも畳み込みません(定数でない値は畳み込みの対象外です)。畳み込み後のフレーム列がRazorと一致することは、静的に綴った別の対で機械的に検証しています。両者ともマークアップフレーム1つを発行し、シーケンス番号以外の差はありません。

残る差は3点あり、うち2点はRazorのソース形式に由来し、1点は本方式側の不足です。Razorは補間を含むテキストを複数のフレームへ分割し、タグ間の改行をマークアップフレームとして発行します。この2点は本方式側の不足ではありません(上記いずれの比較でもRazor側の fixture を1行で綴っているのは、後者を比較から除くためです)。3点目は畳み込み対象のタグです。本方式は allow-list で対象を絞りますが(§2.7(D))、Razorは静的なマークアップであれば対象を制限しません。したがって `Pre["code"]` や `Element("marquee")["x"]` はRazorでは畳み込まれ、本方式では要素フレームとテキストフレームのまま残ります。正しさは変わりません。最適化機会を手放す選択であり、意図した現状です。

加えて、フレーム数が同じでフレーム種別だけが異なる場合があります。吸収するフレームが1つしかない run を畳まない規則(§2.7(D))により、畳み込めない要素の下にある単一の静的テキストはテキストフレームのまま残ります。`Div.Class(_cls)["x"]` は `AddContent(2, "x")` を、Razorの `<div class="@cls">x</div>` は同じ位置に `AddMarkupContent` を発行します。シーケンス番号もフレーム数も一致するため、上記の等価性検査はフレーム種別まで比較して初めてこの差を捉えます。

動的コンテンツ経路(§5.3、Opaque経路)の追加コストは、`RenderFragment` の手書きと同一です。比較対象は、SSCで組んだ要素に、`[ViewPart]` の付かないメソッドが返す `View`(内部は手書きの `RenderFragment`)を子として1つ加えたコンポーネントと、同じ `RenderFragment` を直接埋め込んだRazorコンポーネントです。上表と同じ2つの境界で一致しました。

| 境界 | BlazorCodeFirst(Opaque経路) | 手書き `RenderFragment` |
| --- | --- | --- |
| `BuildRenderTree` 単体 | 104 B | 104 B |
| Diffを含む1レンダリングサイクル | 176 B | 176 B |

生成コードは `AddContent(seq, ViewRuntime.FragmentOf(呼び出し式))` を発行し(`ARCHITECTURE.md` §3.2)、比較対象のRazor側は同じ `RenderFragment` を `@メソッド呼び出し()` で直接埋め込みます。いずれも最終的にBlazorの `RenderTreeBuilder.AddContent(int, RenderFragment?)` へ落ち、この呼び出しがフラグメント用のリージョンを内部で開くため、生成コード側が自らリージョンを書かなくても実行時のフレーム列は一致します。`BuildRenderTree` 単体の104 Bは補間文字列とフラグメントを束ねるデリゲート1個の割り当てであり両者で同一です。`View`/`ViewRuntime.FragmentOf` の間接参照自体はバイト数を増やしません。

### 7.2 差分検知性能

静的シーケンス割当により、Diffing計算量は理論上の最小値 O(|r_t| + |r_{t+1}|) を維持します(`ARCHITECTURE.md` §1.2)。既存の兄弟要素より前へ条件付き要素を挿入するシナリオ(`π` と生成順序の対応が崩れる条件、§1.2)で、N行のキー付きリストに対し実測しました。

| 方式 | シーケンス | リージョン | キー | 編集数 | 破棄された行 |
| --- | --- | --- | --- | --- | --- |
| 本方式(生成コード) | 静的リテラル | あり | あり | **3**(Nに依存しない) | **0** |
| 動的インクリメント | 実行時カウンタ | なし | なし | 3N+3 | N または 1 |
| 動的インクリメント | 実行時カウンタ | なし | あり | **3**(Nに依存しない) | **0** |
| 動的インクリメント | 実行時カウンタ | あり | あり/なし | 3N+3 | N |

N = 10 / 100 / 1000 で本方式は常に3編集であり、計算量の主張は確認されました。

一方、当初この差を「構文位置に固定されたシーケンス番号による」と説明していましたが、実測はそれを支持しません。キーを付けた動的インクリメント方式は、静的割当を一切用いずに本方式と同じ結果(3編集・破棄0)に到達します。上表が測ったのはこのキー付き領域であり、`ForEach` にキーを書いたリストがそこにあります。このシナリオにおいて本方式が動的インクリメント方式に対して優位を示すのは、キーを持たない場合との比較です。`key: null` と書いて降りたリストはその側にあり(§4.2)、降りることの費用がここに測られています。

静的割当が実際に効くのはリージョンが介在する場合です。上表最下段は、本方式のフレーム列と完全に一致します(シーケンス番号のみが異なることを機械的に検証済み)。`OpenRegion` のシーケンス自体が実行時カウンタからずれると、行を含むリージョンごと破棄され、キーは無効化されます。キー照合は兄弟グループ内で行われ、そのグループ自体が移動するためです。`If` / `ForEach` がリージョンを発行する本方式(§5.3)において、リージョンフレームのシーケンスが構文位置に固定されていることは、この意味で本質的です。なお実在のライブラリがリージョンを実行時採番で発行することはないため、この構成は帰属を切り分けるための対照であり、§7が公開する比較対象ではありません。

キーなしの場合に破棄される行数は構造的条件に依存します。バナーの消費するシーケンス数が行のフレーム幅の倍数であるとき、行 `i` は行 `i+1` の位置に一致し、差分はコンポーネントを破棄せずテキストを書き換えます(破棄は末尾の1行のみ)。倍数でないときは全N行が破棄されます。いずれの場合も編集数は3N+3であり、計算量の主張には影響しません。

### 7.3 Wasmバイナリサイズ

パラメータバインディングを含む全機構がリフレクション・フリー(`System.Reflection` / `System.Linq.Expressions` へのランタイム依存ゼロ)であるため、ILトリマーが未使用コードを削除できます。`TrimMode=full`・`ILLinkTreatWarningsAsErrors=true` を有効にした状態で、設計時ゲッター(`Body` / `Chrome`)と未到達の設計時APIのMethodDefがメタデータレベルで除去される設計です。リフレクションベースのバインディングを持つ同等ライブラリ比で、AOTコンパイル後のWasmペイロードを約20〜30%削減(予測値)と見込みます。素のRazor構成との比較ではほぼ同等です。

---

## 8. 関連プロジェクトとの比較

C#によるコードファーストUIの試みは本ライブラリが最初ではありません。ただし、対象プラットフォームが異なるため直接の競合にはあたりません。本章は設計アプローチの対比です。

|                  | BlazorCodeFirst           | Comet                                           | Avalonia.Markup.Declarative               | CommunityToolkit.Maui.Markup     | 手書き RenderTreeBuilder   |
| ---------------- | ----------------------- | ----------------------------------------------- | ----------------------------------------- | -------------------------------- | -------------------------- |
| レンダリング先   | 実DOM(Blazor)           | ネイティブ(MAUIハンドラ)                        | ネイティブ+ブラウザ(Skiaによるcanvas描画) | ネイティブ(MAUI)。ブラウザ非対応 | 実DOM                      |
| プロジェクト状態 | 本提案                  | 2025年7月アーカイブ(概念実証・公式サポートなし) | 活発                                      | 活発                             | Blazor標準(手書きは非推奨) |
| UIモデル         | 宣言的(再評価+差分検知) | 宣言的(実行時評価+リフレクションバインド)       | retained-mode構築の糖衣                   | retained-mode構築の糖衣          | 宣言的(全手動)             |
| シーケンス番号   | コンパイル時確定        | 対象外(Blazor外)                                | 対象外                                    | 対象外                           | 手動管理(破綻しやすい)     |
| 状態の記述       | 素のC#フィールド        | `State<T>` ラッパー+反応ラムダの使い分け        | ViewModel / `StateHasChanged`             | バインディング式                 | 素のフィールド             |
| 実行時の中間表現 | なし(SSC経路)           | UIツリー+リフレクション                         | コントロール実体を保持                    | コントロール実体を保持           | なし                       |
| AOT/トリミング   | 完全適合                | リフレクション依存                              | 適合                                      | 適合                             | 適合                       |

DOMネイティブである点が最も大きな違いです。Avaloniaはブラウザ上でも動作しますが、それはSkiaによるcanvasへの描画であり、DOMを持ちません。SEO、アクセシビリティツリー、CSSエコシステム、SSR/プリレンダリングはcanvas描画には適用できません。BlazorCodeFirstは実DOM/HTMLへ宣言的UIを射影するため、これらのWeb標準の資産をそのまま利用できます。

Blazor差分検知との構造的整合も本ライブラリに固有の課題です。retained-mode系(Avalonia.Markup.Declarative、MAUI.Markup)はコントロール実体を保持・変異させるため、シーケンス番号問題自体を持ちません。一方、Blazor上でコードファーストを試みる場合この問題は不可避であり、本ライブラリはこれをRazorコンパイラと同型の方式で解決します。手書き `RenderTreeBuilder` や実行時ツリー方式では、この問題が正しさと性能の両面で破綻要因となります。

そして宣言的セマンティクスとゼロ中間表現が両立します。毎レンダリングでUI全体を再評価する宣言的な書き味は、実行時ツリー構築方式(Cometがこの型)ではGCプレッシャーという恒常的コストを伴います。コンパイル時生成方式では、同じ書き味を実行時の中間オブジェクトなしに得られます。

---

## 9. 対象範囲の境界

本設計図が扱うのは、本ライブラリの本筋(コンパイル時の静的シーケンス割当)へ直結する範囲だけです。デザインツール連携やCSSフレームワーク向けアダプター等の周辺構想は、本筋の設計とは独立した別プロダクトの検討事項であり、対象外とします。

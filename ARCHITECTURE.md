# BlazorCodeFirst Architecture

**Internal architecture: the compilation algorithm, sequence assignment, memory layout**

Target environment: .NET 10 (baseline), .NET 11 (conditional features)

> For background, goals, and usage, see `DESIGN.md`.

---

## 0. Notation and assumptions

Symbols are used only where this design's core needs to be stated precisely: the sequence-number stability conditions (§1.2). There, basic set and mapping notation is used (`f : A → B` for a mapping, `|X|` for cardinality). Everywhere else is ordinary prose.

Language and runtime features this specification depends on:

| Feature                                                | Requirement                            | Use                                          |
| ------------------------------------------------------- | --------------------------------------- | --------------------------------------------- |
| Source Generator member generation into a partial class | Every supported version (a mature, standard feature) | Generating `RenderView` (§2)          |
| IL trimming / Native AOT                                 | .NET 10                                 | Removing the inert API and unused code (§5)   |
| Union types / `closed` hierarchies                       | C# 15 / .NET 11 (conditional)           | `ViewNode`'s closed-world definition (§6)     |
| Runtime Async                                            | .NET 11 (conditional)                   | Lightening the event pipeline (§4.3)          |

That the core mechanism does not depend on any particular bleeding-edge language feature is a deliberate property of this design. Alternative architectures considered and rejected (an Interceptor-based approach, a runtime ref-struct-tree approach) and why are recorded in Appendix B.

---

## 1. Abstract mathematical model and formal definitions

### 1.1 State and projection

Let `S` be a component's state space, and `s_t` (`s_t ∈ S`) the state at time `t`. Let `R` be the set of Blazor's internal rendering trees (frame sequences), and `r_t` (`r_t ∈ R`) the frame sequence generated at time `t`. `R` and `r_t` are used in the diff-detection stability condition (§1.2).

At build time, the Source Generator compiles a design-time UI expression into "a function that takes a state and returns a frame sequence" (typed as `S → R`). Only this generated function runs at run time, and `r_t` is the result of applying it to state `s_t`. The UI expression itself (the design-time syntactic entity) is never evaluated at run time. Contrasted with Razor: the Razor compiler takes this same input as markup, where BlazorCodeFirst takes it as a C# expression.

It is a convention that the generated function is pure — it depends only on state and carries no side effects (single-direction data flow, §4.1). A state mutation inside a design-time expression (`BodyComponentBase.Body` or `ChromeLayoutBase.Chrome`) is covered by diagnostic BCF3001. BCF3001's initial detection scope is limited to a statically identifiable direct write to a component instance member (field assignment, property assignment, compound assignment, the increment/decrement operators). A mutation inside a deferred handler argument, such as a `Button`'s onClick lambda, is excluded because it runs after rendering; the excluded positions are defined by Appendix A's BCF3001 row. Complete detection of a side effect reached through an arbitrary method call (e.g. an async chain) is not guaranteed in the initial slice.

### 1.2 Rendering-tree equivalence and diff detection

Each frame `n ∈ r_t` in `R` carries a sequence number `seq(n) ∈ ℕ`. Let Blazor's diff operator be

```
Δ : R × R → Patch
```

and `Δ(r_t, r_{t+1})` is applied to the DOM. Blazor's diff algorithm walks both trees simultaneously from the start, and judges frame identity (retain/insert/delete) purely by comparing sequence numbers for equality and ordering.

**Theorem 1 (sequence stability condition)**
For `Δ` to run at minimum cost O(|r_t| + |r_{t+1}|) and preserve the state of semantically identical nodes, the following must hold for every semantically identical node pair `(n, n′)` (`n ∈ r_t`, `n′ ∈ r_{t+1}`):

```
seq(n) = seq(n′)                                   … (1)
```

**Corollary 1**: A sufficient condition to satisfy condition (1) is that `seq` is a function of the syntactic position in source code rather than the run-time generation order. Letting `π(n)` be the syntactic position of the expression node that generated the frame, there exists an injection `σ` such that:

```
seq(n) = σ(π(n)),   σ : Π → ℕ is an injection      … (2)
```

In this approach, `σ` is constructed by the Source Generator at build time and embedded into the generated code as literal constants, so condition (2) is satisfied structurally. In contrast, a runtime-increment approach (`seq(n) = generation order`) violates condition (1) the moment conditional rendering or element insertion breaks the correspondence between `π` and generation order, degrading to an O(n) walk.

What follows a violation of condition (1) splits on whether a key is present (measured figures: `DESIGN.md` §7.2). Without a key, the frames from the point that should have matched onward are misjudged as "delete plus fresh insert," and the internal state of the reconstructed component (in-progress text input, for example) is lost. That range still depends on a structural condition, though: when the offset is a multiple of a node's frame width, a later node matches at a position shifted by one, so the loss is confined to the tail and the rest becomes a text rewrite. With a key, on the other hand, key matching within the sibling group holds, so state survives even when the sequence shifts. State loss, in other words, only occurs where a condition-(1) violation and the absence of a key coincide — it does not follow from a condition-(1) violation alone.

State preservation depends on the sequence number being fixed to syntactic position specifically where a region (`OpenRegion`, §5.3) is involved. If a region's own sequence shifts, the whole region is discarded, and because key matching only operates within a sibling group, it cannot rescue the state. In this design, where `If` / `ForEach` each emit a region, condition (2) is essential in exactly this sense.

---

## 2. The compilation algorithm

### 2.1 The overall pipeline

```
[User code]                          [Source Generator]
partial class C :                    ① partial verification, Body discovery
BodyComponentBase                 ② SSC classification (§2.3)
  View Body => …        ──AST──▶    ③ DFS-order sequence assignment (§2.2)
  [ViewPart] View F() => …         ④ Generating RenderView(RenderTreeBuilder)
                                        — embedding static seq constants
                                        — transplanting dynamic expressions/lambdas as syntax
                                        — inline-expanding [ViewPart]
```

The output is a `RenderView` override in the same partial class, called from the base class's (`BodyComponentBase`, or a layout's `ChromeLayoutBase`) `BuildRenderTree`. Both the design-time expression (`BodyComponentBase.Body` or `ChromeLayoutBase.Chrome`) and the design-time API are unreachable at run time, and the IL trimmer removes them in an AOT build. The design-time API here means every member of `Html` and `Decorations`, and every member of the design-time inert types `View` / `ComponentView<T>` / `ElementView` (Appendix A, BCF3014). The design is verifiable by checking, via `System.Reflection.Metadata`, that no MethodDef remains; the trim tests carry out that verification.

A design-time expression's getter **must reach exactly one `return`**. The three spellings `=> expr` / `get => expr` / `get { return expr; }` are identical, and each generates the same `RenderView`. Local declaration statements and expression statements may precede that `return`. This is the same shape `ForEach`'s `content` accepts (§2.3 Transplantable), and the written statements are transplanted ahead of frame emission. One implementation reads this shape: `RenderExpressionAnalyzer.TryReadTransplantableBlock` answers for both the getter and `content`. Being able to place statements does not permit side effects — a state mutation is still BCF3001. Four shapes remain as BCF1004: a second `return`; a native control construct; a local with a generator-reserved name (the `__bcf_` prefix and `__builder`); and an auto-property that declares no getter body to translate. A re-abstraction (`abstract override`), and a partial property with no implementing part, are excluded — CS9248 names the cause of the latter. A design-time expression is inert syntax never evaluated at run time, and this constraint is simply the premise of "translating syntax statically."

Hand-writing an override of `RenderView` instead of a design-time expression is legal, and is the escape hatch for a body the SSC subset cannot express. In this case the generator produces nothing — generating one would duplicate the member name into CS0111, leaving the author no option but to delete their own code. The design-time expression goes unused, and BCF1004 is not reported either.

The declaration shape recognized as a BlazorCodeFirst component is a top-level `partial class`. Generics (`partial class Foo<T>`) are supported, and the generated part repeats the same type parameter names (it does not repeat the constraint clause — a constraint belongs to the type parameter, so having it on one declaration is enough). A nested type is rejected by BCF1005. A `record` can only inherit `object` or another `record` (CS8864), so it cannot be a BlazorCodeFirst component.

### 2.2 Sequence assignment

`Body`'s expression tree `e` is walked depth-first (preorder), reserving a disjoint sequence range for each UI node. `counter` is not an absolute offset in the source code, but an integer assigned by the syntax tree's logical preorder walk order (a preorder ordinal). This guarantees that a change to a comment or whitespace never affects sequence-number stability.

```
procedure Compile(e: ExpressionTree, model: SemanticModel) → RenderView:
    counter ← 0
    code ← ∅
    for each node v in DFS-Preorder(e):
        match Classify(v, model):
            case Factory(kind) | Decorator(kind):
                w ← EmittedWidth(v)                 // the frame count this node emitted (emission is authoritative, §2.7(D))
                code += EmitFrames(kind, v.Args, seqBase: counter)
                counter ← counter + w
            case Combinator(If | ForEach):
                code += ExpandCombinator(v, ref counter)   // §2.4
            case ViewPartCall(m):
                code += Compile(Body(m), model)            // inline expansion (recursive)
            case Transplantable(stmt):                     // a native if/foreach, etc.
                code += WrapInRegion(Transplant(stmt), seq: counter); counter += 1
            case Opaque(expr):                             // a non-[ViewPart] View-returning call, etc.
                code += WrapInRegion(EmitFragmentOf(expr), seq: counter); counter += 1
                report BCF2001(v)
    return code
```

The pseudocode above is written as a per-node loop, but the unit of folding is **a run of consecutive siblings** (§2.7(D)), and the whole run is emitted as one `AddMarkupContent`. So it is the emission itself, not the node kind, that determines the width (§2.7(B)). No implementation computes the width independently, and none should be added.

`FrameWidth` counts only the `RenderTreeBuilder` calls that consume a sequence argument, and excludes calls with no sequence argument such as `CloseElement`/`CloseRegion`. It is determined by the node kind and by whether that node can be folded. For example: a `Span` with no children = 1 [`OpenElement`]; a `Span` with one **dynamic** string child (`Span[$"...{x}"]`) = 2 [`OpenElement` + `AddContent`]; a `Button` with one onclick attribute = 3 [`OpenElement` + `AddAttribute` + `AddContent`]. An event blocks folding, so this is the width even when the `Button`'s child is constant. A `Span` with one **constant** string child (`Span["..."]`), by contrast, is itself foldable, so its width is 1 (a single `AddMarkupContent`).

Within a decoration chain, `class` is statically composed into the parent element's `class` attribute, so adding `.Class` never increases the frame count (`.Class("a").Class("b")` folds into a single `AddAttribute`). The folded value is assembled after first clearing away the terms readable at compile time. A constant `null` term drops; adjacent constant strings fold into one literal; and only when two or more terms remain does it call the `private static` join the generated class keeps for itself. Because this join skips `null` terms at run time, the separating space disappears along with the term (#236). `AddAttribute` is still emitted even when every term drops, so the frame width is determined purely by the number of decorations and never moves with the value (#234).

An attribute or event decoration other than `class` (`.Href` / `.Attr` / `.OnClick` / `.On`, and so on) adds one frame per decoration (details in §2.7(A)). The exception is `.Bind`, which adds two — an attribute frame and an event frame — per instance; any number can be placed on the same element, and these two frames stack that many times (§2.7(A)). A dynamic argument (an interpolated string, a state reference, an event lambda) is never evaluated — it is transplanted as syntax into `EmitFrames`'s output. Because it is generated inside the same partial class, `private` access via `this` is preserved.

When a value expression is transplanted into generated code, a resolved type name is normalized to its fully-qualified form starting with `global::`. An unresolved type name becomes BCF3015, since a spelling that depends on the original file's `using`s or namespace cannot be transplanted safely as-is. A type reference the author already wrote from `global::`, however, is left to ordinary C# name resolution, since it does not depend on lexical context. A generic type's outer type and each type argument are judged independently. Only a deconstruction declaration's `var` is left exactly as written, because the language allows no type at all before a parenthesized designation — there is simply no shape there in which a normalized name could be written (#342).

`Html.Fragment` (wrapperless grouping) opens no frame of its own, so its `FrameWidth` is the sum of its children's `FrameWidth` (the same shape as a `[ViewPart]` expansion node with no local variables). When every child is foldable, though, the whole fragment becomes one run and its width is 1 (§2.7(D)). `Html.Raw` (trusted raw HTML injection) is a single frame that only emits one `AddMarkupContent`, `FrameWidth` = 1 (the same shape as `AddContent` for a childless string content node). Neither opens an element/component frame, so neither can serve as the root of a `ForEach`'s `content` (BCF3003), nor can either be decorated (BCF3008, details in §2.7(A) and Appendix A).

That decoration is not allowed is also expressed in the type system: a decoration is an extension of `ElementView`, and `Fragment` / `Raw` are `View`, so it is CS1929. BCF3008 is still reported because this CS1929 never reaches the author. A component whose design-time expression cannot be translated has no `RenderView` generated, and the class always carries the declaration-stage error CS0534, so `csc` never proceeds to binding the method body. A real-MSBuild measurement taken before `RejectedDecorationScanner` existed found that fixture `Bcf3008Host` reported only CS0534 and BCF1003 — no CS1929 appeared. Now that BCF3008 is reported, the same fixture reports it too. BCF1003 reaches the author in the same build. Only a generator diagnostic can get past this cutoff.

### 2.3 The statically sequenceable subset (SSC)

Condition (2)'s `σ` cannot be constructed for arbitrary C# code, because the call graph is only settled at run time. The scope of the analysis is classified into the following three tiers:

**SSC (fully static)**: subject to static sequence assignment.
- SSC-1: direct writing of an element helper/decoration, and a direct call to `Component<T>()`, `Fragment`, or `Raw`, in a `Body` body or a `[ViewPart]` method body
- SSC-2: the `If(cond, then, otherwise)` combinator (both branches must be inline lambdas)
- SSC-3: the `ForEach(source, key, content)` combinator (`content` an inline lambda, `key` an inline expression lambda or a written `null`), and its sugar, the spread `[.. <source>.Select(<inline expression lambda>)]` inside a child list (which folds into the same node, matching down to not emitting `SetKey`)
- SSC-4: any nesting of SSC-1 through SSC-3, and static inline expansion of a `[ViewPart]` call

**Transplantable (syntax transplant)**: a statement is transplanted whole into generated code and wrapped in a boundary region (§2.5). One shape is accepted: a block containing local declaration statements and expression statements, ending in exactly one `return <SSC expression>;`. It can be written in three positions: a block-bodied lambda written in `ForEach`'s `content`, a design-time expression's (`Body` / `Chrome`) getter, and a `[ViewPart]`'s body. In the first the statements fall inside the loop; in the second, ahead of `RenderView`'s frame emission; in the third, into the expansion site. A transplanted statement contains no call that takes a sequence argument, so the sequence width is the same as writing a single expression. Multiple `return`s and a native `if` / `foreach` / `switch` are each not accepted, since each would need its own sequence space. The diagnostic splits by position: `content` is BCF3004, the getter is BCF1004, and `[ViewPart]` is BCF1002.

A local a `[ViewPart]`'s body binds into an enclosing scope receives a generated name only at that definition (#336, #343). Because the definition's body is duplicated at every call site, a name the author wrote can end up declared twice in one generated scope. There are two binding paths: a local the leading statement declares, and a designation in the return expression (a pattern variable, `out var`, a deconstruction). An expression body that has only the latter also receives this renaming. The inside of a lambda is excluded. Because both an `If` branch and a `ForEach`'s content fall into their own braces in the generated code, the two expansions never meet in one scope. The renaming uses the same mechanism as an iteration variable: both the declarator's identifier and its references are carried by the same hole, and the expansion casts the name. The design-time-expression side keeps the written name as-is, because within a single design-time expression the author's nesting and the generated code's nesting coincide, so the written name is always legal there (sibling blocks become sibling generated scopes, and if a getter's local and a block's local shared a name, the author's own file would already be CS0136).

There are two positions that accept a local from the enclosing scope, and both are headers of lowered syntax (#361). An `If`'s condition falls into the generated `if`'s header, enclosing both branches. `ForEach`, and the source of its sugar `..source.Select(…)`, falls into the generated `foreach`'s header, enclosing the loop body; `key`'s body falls into that loop body's `SetKey`, so it reads the same way. Registration uses the same mechanism as a transplanted block (`ViewPartBodyContext.PushTransplantedScope`), and since both a design-time expression and a `[ViewPart]` read the same check, the two positions close together.

Acceptance is limited to these two, and simple containment is not enough to judge it — the reason differs by position. A component's slot has its contents wrapped in a single `RenderFragment` lambda, so a local declared in one slot reaches neither a sibling slot nor a sibling parameter. An element's siblings, conversely, do line up into one block, but not in the author's order: the class channel falls ahead of the attribute loop, event and bind decorations fall after it, and a run of constant children folds into one markup frame (§2.7). In the author's file, `out var` scopes all the way to the enclosing statement, so both shapes are valid C#. `LoweredHeaderLocalTests` pins both sides of the boundary.

**Opaque (runtime evaluation)**: a call to a `View`-returning method with no `[ViewPart]`, an indirect call through a delegate, and so on. The SG cannot analyze the inside, so it transplants the call expression into generated code and renders, at run time, the `RenderFragment` the returned `View` wraps. Notified by diagnostic BCF2001 (Info).

This path rests on one premise, and that premise decides its scope: the only spelling that puts a fragment into a `View` is `implicit operator View(RenderFragment?)`, and every member of the design-time surface returns a default value (§3.2). So a `View` built from the surface carries no fragment, and putting it on the Opaque path still renders nothing. When the call target's source declaration is in the current compilation and its body references the design-time surface, this does not fall onto this path — BCF3030 (Error) stops it instead. What is accepted as Opaque is a body that does not reference the surface, and a declaration that cannot be read. The latter carries residue that cannot be judged, recorded in Appendix A's BCF2001 row.

いずれの階層でも正確性は保たれます。失われるのはTransplantable/Opaque領域内部の静的差分最適化のみです。

### 2.4 Static sequence-space separation for conditional branches

For SSC-2's `If`, a disjoint static sequence range is reserved for each branch:

```
If(condition, then: T₁, otherwise: T₂)

Assignment:  seq(boundary region)  = k
             seq space(T₁)         = [k+1,  k+1+W(T₁))
             seq space(T₂)         = [k+1+W(T₁), k+1+W(T₁)+W(T₂))
```

Conceptual shape of the generated code:

```csharp
__b.OpenRegion(k);
if (condition)
{
    /* T₁'s frame sequence: seq ∈ [k+1, k+1+W(T₁)) */
}
else
{
    /* T₂'s frame sequence: seq ∈ [k+1+W(T₁), …) — does not overlap T₁ */
}
__b.CloseRegion();
```

When `condition` transitions `true → false`, `T₁` and `T₂`'s sequences never intersect, so the Blazor engine correctly detects this as "exclusive discard of the whole segment and fresh generation" rather than "rewriting the same slot" (which would carry over the wrong state). This satisfies Theorem 1's condition (1) in a way consistent with branch semantics — nodes in different branches are not semantically identical.

`ForEach` (SSC-3) expands into a `foreach`, assigning a single static sequence space to the template `content` and identifying identity across iteration instances with `SetKey(key(item))`. The division of responsibility — sequence carries "syntactic position within the template," key carries "data identity" — and the minimal patch under a list mutation are shown as input/output examples in §2.7(B).

### 2.5 Sequence-space separation via regions

A Transplantable / Opaque region `D` is wrapped in a region whose boundary carries a single static sequence:

```csharp
__b.OpenRegion(seq_D);           // seq_D was assigned statically
__b.SetKey(runtimeKey);          // for Opaque, a runtime key where needed
/* D's content */
__b.CloseRegion();
```

Because a Blazor region isolates its sequence space, `D`'s internal dynamism never propagates out to the surrounding diffing.

### 2.6 Hot Reload compatibility

Development-time edits are classified by mapping them to .NET Hot Reload's (EnC's) edit classes.

A change to a `Body` expression or a `[ViewPart]` body appears as a method-body swap in the regenerated `RenderView`. A method-body update is an edit class EnC supports stably. Adding a new `[ViewPart]` method is a member addition to an existing type, likewise within the supported range. A rude edit, such as a change to the component class's signature, requires an application restart, the same as with a Razor component.

The semantics of the first render after a reload follow directly from §1.2. When an edit changes the syntactic-position mapping `π`, the old and new `σ(π(n))` generally no longer agree (condition (1) fails to hold), so the affected component's frame sequence is treated by diff detection as "exclusive discard and fresh generation." Because the component instance itself is retained, C# field state survives, while DOM-local state (focus, scroll position, and so on) is lost. This is the same semantics as editing a Razor file, and needs no additional specification.

The application path also rides on the Blazor standard. Because the generated code is an ordinary method on a `ComponentBase`-derived type, Blazor's own `MetadataUpdateHandler`-driven re-render-after-update mechanism works unchanged. This design's only tooling-specific dependency is the single point that the Source Generator re-runs during an edit session and its updated generated code is applied through EnC. Behavior can differ across Visual Studio / `dotnet watch` / Rider, so each environment needs its own confirmation. Appendix C shows the development-time fallback for a case where a specific environment is found not to carry a re-run through to EnC.

### 2.7 Input/output specification for the key transforms: decoration folding, lists, part reuse, static folding, frame decorations

This design turns on five transforms: the decoration chain, lists, `[ViewPart]`, static subtrees, and non-attribute frame decorations (plain element emission is not included here). At the same level of detail as §2.4's `If`, each one defines exactly which input turns into which generated code.

**(A) Folding a decoration chain. Input: a chain of decorations / Output: `class` folds, other attributes and events emit 1:1 frames**

A decoration method is statically composed into the owning element's attributes and events, and adds no wrapper node. `class` is special: however many `.Class` (or `.Attr("class", …)`) are chained, they fold into a single `class` attribute, producing no extra attribute frame. An attribute or event other than `class` (`.Href` / `.Attr` / `.OnClick` / `.On`, and so on) is each emitted 1:1 as its own independent attribute/event frame, and a duplicate binding of the same attribute or event is diagnosed by BCF3010. Three spellings reach `class`, and only two of them fold: `.Bind("class", …)` does not join the channel and emits its own attribute frame, so combining it with a decoration emits two `class` attributes — diagnosed by BCF3024.

```csharp
// Input (a design-time C# expression)
Button
    .Class("btn")
    .Class("btn-primary")
    .OnClick(() => Save())["Save"]
```

```csharp
// Output (generated code): the two .Class calls fold into one class attribute; .OnClick is its own frame
__b.OpenElement(k,   "button");
__b.AddAttribute(k+1, "class", "btn btn-primary");
__b.AddAttribute(k+2, "onclick", /* () => Save() */);
__b.AddContent(k+3, "Save");
__b.CloseElement();
```

This `Button`'s `FrameWidth` is 4 (`OpenElement` + the `class` attribute + the `onclick` event + `AddContent`). Chaining `.Class` any number of times never increases the frame width, but adding one decoration other than `class` increases the frame width by one. A wrapper-node approach (one that generates a dedicated wrapper element per decoration) would have each decoration increase the DOM node count itself, but this design composes every decoration into the owning element's attributes and events, so DOM depth never increases. This asymmetry is the basis for diff detection's sequence assignment staying stable as decorations pile up.

An event frame's value is a call to `EventCallback.Factory.Create`, with the handler expression transplanted into its argument. When `.On<TArgs>` resolved, its type argument is written out as `Create<TArgs>` (a written type argument and an inferred one are not distinguished — they are two spellings that resolved to the same overload, and writing out only one would translate the same call two different ways). The transplant site has no parameter that was giving the handler its type at the call site. Because `Create` is overloaded, without a type argument the method group cannot infer `TValue`, and a lambda argument with no type annotation binds to `object` — both become CS1503 inside a file the author never wrote, so the call site's type argument is written out as-is to keep inference off the path (#371). A spelling with no type argument (a bare `.On` and an event shortcut) has no type to write out, and stays plain `Create`. That every handler spelling this surface allows binds in the generated code is pinned by `HtmlDecorationGeneratorTests.On_WithATypeArgument_NamesItOnCreate`, which compiles the generated output.

The one exception to 1:1 is two-way binding. `.Bind` emits one attribute frame and one event frame, so this decoration's `FrameWidth` is 2. Writing an event modifier on a bound event adds one more per modifier (§2.7(A)'s event-modifier item). When the bound attribute is `value` or `checked`, it also calls `SetUpdatesAttributeName` once; since this takes no sequence argument, it adds no frame (it only records the attribute name to resynchronize into the immediately preceding attribute frame). It is limited to these two attribute names because the only thing the client returns is that element's own `value` (or `checked` for a checkbox) as `EventFieldInfo` assembles it. `RenderTreeUpdater` writes that value into the frame this call named. Naming any other attribute overwrites an unrelated frame on a form element, leaving the real attribute stale, and on a non-form element `EventFieldInfo.fromEvent` returns `null`, so the call itself is a no-op. Because the record target is the immediately preceding attribute frame rather than the element, placing two bindings on the same element leaves each keeping its own name, with no overwrite and no lost resync (measured). Any number of bindings may be placed on the same element; the model side also holds an element's bindings as a collection. A name collision is reported by BCF3010, and when the bound attribute is `class` and the same element also carries a class-channel decoration, BCF3024 reports it. This was once rejected by BCF3021, but that was withdrawn because its basis was wrong (付録B.5).

```csharp
// Input (a design-time C# expression)
Input.Type("text").Bind("value", "oninput", () => _name)
```

```csharp
// Output (generated code): two frames, the attribute frame and the event frame, plus the resync record
__b.OpenElement(k,   "input");
__b.AddAttribute(k+1, "type", "text");
__b.AddAttribute(k+2, "value", _name);                  // attribute frame
__b.AddAttribute(k+3, "oninput", EventCallbackFactoryBinderExtensions.CreateBinder(
    EventCallback.Factory, this, __value => _name = __value, _name));   // event frame
__b.SetUpdatesAttributeName("value");                   // takes no sequence argument
__b.CloseElement();
```

`CreateBinder` is written as a static extension-method call because the generated file carries no `using`, and Razor's instance syntax (`EventCallback.Factory.CreateBinder(…)`) would be CS1061. The same normalization is applied to an author-written extension method too (§2.2). In the shape that writes the setter explicitly, `(Action<T>)(setter)` fills the `__value => …` position. For an async setter, `RuntimeHelpers.CreateInferredBindSetter(callback: setter, value: <current value>)` fills it. Either shape passes the current value as `CreateBinder`'s last argument, and the frame count does not change.

`.Bind` never takes part in (D)'s static folding. Its value is a read of a field or property, so it can never be a compile-time constant. But it is not the value's non-constancy that stops folding — the predicate itself does. `StaticMarkupSerializer.IsFoldableElement` returns not-foldable for any element whose binding collection `ElementNode.Bindings` is non-empty. Leaving it to a value check would let this predicate produce, in principle, output where the binding silently drops and only a plain attribute remains.

A component-side `.Bind` carries none of this asymmetry. The derived `{name}Changed` and `{name}Expression` stack as ordinary parameter frames, so the frame width is two `.Param`'s worth, or three for a type that declares `{name}Expression` (the component frame-width formula at the end of (D) applies as-is). Nor is there anything corresponding to the element side's `SetUpdatesAttributeName`, since it is the bound-to component that owns the DOM, not this call site.

An event modifier (`.PreventDefault` / `.StopPropagation`, Razor's `@onwheel:preventDefault`) attaches to the immediately preceding event, and stacks one frame of its own right after that event's frame. Its name is `__internal_preventDefault_<event name>` or `__internal_stopPropagation_<event name>`, and its value is `bool`.

```csharp
// Input
Div.Ref(r => _el = r).On<WheelEventArgs>("onwheel", Zoom).StopPropagation().PreventDefault()

// Output
__b.OpenElement(k, "div");
__b.AddAttribute(k+1, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(this, Zoom));
__b.AddAttribute(k+2, "__internal_preventDefault_onwheel", true);   // always this order, not the chain's order
__b.AddAttribute(k+3, "__internal_stopPropagation_onwheel", true);
__b.AddElementReferenceCapture(k+4, r => _el = r);                  // after the modifiers
__b.CloseElement();
```

Three points were decided about this shape.

First, this writes `AddAttribute` directly rather than calling the framework's `AddEventPreventDefaultAttribute`. Those two are not members of `RenderTreeBuilder` but extension methods on `Microsoft.AspNetCore.Components.Web`'s `WebRenderTreeBuilderExtensions`, and their contents are the identical `AddAttribute` call written here (measured). Calling them would pull that assembly into the shipped package's dependency set, unraveling the fine-grained references #23 chose and settling, as a side effect, a question #156 has not yet decided — so the name is written by hand instead. The cost is that a framework-internal name ends up in the generated code, and `EventModifierParityTests` buys that back: it reads the generated spelling from `RenderViewEmitter`'s constants and cross-checks it against the frame the extension method emits, so a change to the naming convention fails a test rather than becoming a silent no-op.

Second, what triggers emitting the call is not the value but whether it was written at the call site. Write nothing and nothing is emitted. Write `.PreventDefault(false)` and the call is emitted, consuming one sequence number, and the framework side drops the frame (measured). This is a straightforward application of the rule Appendix D already states for a constant `false` `bool`: the number attaches to the call that was issued, not to the frame that resulted. The value may be a runtime expression, handled the same way as `.Attr(name, bool)`.

"The immediately preceding event" refers to any decoration that writes an event — `.On`, an event shortcut, and `.Bind` alike. The two channels record no order relative to each other, so only the syntax can answer which was written later. The judgment walks the chain leftward, stepping over decorations that produce no event (`.Class` / `.Attr` / other modifiers), and asks which channel the first event-producing decoration it finds wrote to. The modifier attaches to the end of that channel. The answer matches the end because decoration classification parses its own receiver first: at the point a modifier is reached, only what was written to its left is on the node, and the nearest event-producing decoration to the left is the one most recently stacked onto that channel.

The binding-side output places the modifier after `SetUpdatesAttributeName`. Because this call records into the immediately preceding attribute frame, inserting a modifier between it and the bound event frame would put the resync attribute name on the modifier's frame instead (#370).

Third, position needs no new rule. A modifier is an attribute frame, so unlike the three in (E) it falls inside the attribute range. Since `AssertCanAddAttribute` refuses to place an attribute after a reference capture, it must come before `.Ref` — but the emitter already stacks the capture only after every attribute, event, and binding has been emitted (E), so this ordering is satisfied structurally. Whatever order the author writes the chain in, the output follows the order above.

**(B) `ForEach`. Input: a list mutation / Output: the minimal patch under key matching**

`ForEach` (SSC-3) expands into a `foreach`, assigning a single static sequence space to the template `content` and identifying identity across iteration instances with `SetKey(key(item))`. Sequence carries "syntactic position within the template," key carries "data identity" — the two responsibilities are orthogonal.

```csharp
// Input
ForEach(_items, key: t => t.Id, content: item =>
    Div.Class(item.Done ? "task done" : "task")[Span[item.Title]])
```

```csharp
// Output (generated code): the template's seq stays the same across iterations; identity is the key's job
__b.OpenRegion(k);
foreach (var item in _items)
{
    __b.OpenElement(k+1, "div");                        // Div (content's root element): seq ∈ [k+1, k+1+W(content))
    __b.SetKey(item.Id);                                // ← attach "immediately after" opening the root element
    __b.AddAttribute(k+2, "class", item.Done ? "task done" : "task");
    __b.OpenElement(k+3, "span"); __b.AddContent(k+4, item.Title); __b.CloseElement();
    __b.CloseElement();
}
__b.CloseRegion();
```

In Blazor's `RenderTreeBuilder`, `SetKey` attaches a key to "the frame of the element/component currently open" (the same shape as Razor's `@key`). The key must therefore be emitted **immediately after** opening `content`'s **root element/component**. Calling it before `OpenElement` — while the parent is still a region — throws `InvalidOperationException: Cannot set a key on a frame of type Region.` at run time. It follows that `ForEach`'s `content` must **have a single element or component as its root**, because a key can only be placed on an element or a component. A shape whose `content` root becomes a region (a bare `if`/`ForEach`/`switch`, and so on) cannot take a key, and is notified by diagnostic BCF3003 (Error). `Html.Fragment` (wrapperless grouping) and `Html.Raw` (trusted raw HTML injection) fall under the same constraint, since neither opens a single element/component frame, and neither can serve as `content`'s root (BCF3003). A nested keyed list wraps the inner loop in a container element (e.g. `content: o => Div[ForEach(o.Items, …)]`) — the same constraint as Razor, where `@key` cannot be placed directly on `@if` and needs an element to wrap it.

This not-keyable judgment is made at two layers, and the two agree: both the template-walking layer (`KeyabilityResolver.ResolveRootKind`) and the post-static-expansion tree layer (`ViewPartExpander.IsKeyableRoot`) limit a keyable root to an element or a component. `KeyabilityResolverTests` pins, per type, how each node type is classified.

Handling an unknown node type is deliberately asymmetric between these two layers. `IsKeyableRoot`'s default of `false` tilts the keyability judgment toward the safe side (not keyable) even as new node kinds are added. `RenderViewEmitter.EmitNode` / `KeyabilityResolver.ResolveRootKind` / `ViewPartExpander.ExpandNode`, on the other hand, throw on an unknown node type, never letting a missed case pass silently. The division is: frame emission and root-kind resolution carry the contract "an unknown node type is caught early as a bug"; `IsKeyableRoot` carries the default "an unknown node type is treated as not keyable."

The only implementation that determines sequence width is emission itself. Each `Emit*` returns the cursor it advanced, and a sibling's starting position is that return value. So adding a new node kind means adding exactly one case, in `RenderViewEmitter.EmitNode`, and a missed case is caught by the exception.

What guards the sequence arithmetic is a property of the emitted text itself: whatever shape the tree takes, the sequence arguments appearing in generated code are a dense run of `0..N-1` in order of appearance, because every node kind always writes the number it reserved into the text. `If` reserves both branches and emits both; `ForEach` reserves the content width and emits content. A slot continues the outer flat counter, and `CloseElement` / `CloseRegion` / `CloseComponent` / `SetKey` consume none. `RenderViewEmitterSequenceTests` checks this against a corpus covering every node kind. A comparison against an independently computed width only looks at the total, so it would pass two offsetting errors and an overlap in `If`'s branch ranges — but this property catches both. This density, by the way, is a property of this implementation's assignment scheme, not a Blazor requirement; all Blazor requires is that a sequence number is stable relative to syntactic position.

`RenderFragmentContentNode` consumes exactly one sequence number always, regardless of whether the `RenderFragment?` is non-null. A sequence-consuming `AddContent` call is always required, and the region frame it opens exists only when the value is non-null.

Trace the output patch when the input mutates from `[A, B, C]` to `[X, A, B, C]` by a leading insertion. Because the template's sequence number is the same across every iteration and identity is carried by the key, Blazor matches keys `A, B, C` against the existing frames (preserving row state and the DOM subtree) and inserts only the one row for `X`. If the key were index-derived instead, position 0 would be misread as "A → X changed," position 1 as "B → A changed," and so on — every row would be rewritten and each row's local state (focus position, and so on) would be lost. Key carrying "data identity" and sequence carrying "template position" is what makes this minimal patch and state preservation hold at the same time.

**(C) `[ViewPart]`'s static inline expansion. Input: a part call / Output: direct expansion into a contiguous seq**

A `[ViewPart]` method call is inline-expanded, body and all, at the call site (§2.2's `ViewPartCall` case). Neither a method call nor a region boundary is generated, and the sequence number stays contiguous with the surrounding body. Arguments are transplanted as syntax.

```csharp
// Input
protected override View Body =>
    Div[Toolbar("My App"), Span["Body"]];

[ViewPart]
private static View Toolbar(string title) =>
    Div.Class("toolbar")[Span[title]];
```

```csharp
// Output (generated code): Toolbar is inline-expanded, and seq runs contiguously from 0
__b.OpenElement(0, "div");                              // Div (Body's root element)
//   ↓ start of Toolbar("My App")'s inline expansion (no region boundary)
__b.OpenElement(1, "div");                              // Div (Toolbar's body)
__b.AddAttribute(2, "class", "toolbar");
__b.OpenElement(3, "span"); __b.AddContent(4, "My App"); __b.CloseElement();  // the title argument, transplanted
__b.CloseElement();
//   ↑ end of Toolbar's expansion
__b.OpenElement(5, "span"); __b.AddContent(6, "Body"); __b.CloseElement();
__b.CloseElement();
```

A `[ViewPart]` call produces the same frame sequence and sequence range as writing its body directly at the call site. Neither runtime dispatch nor region isolation is involved. By contrast, a `View`-returning method with no `[ViewPart]` is treated as Opaque (§2.3): wrapped in a region, rendered at run time as a `RenderFragment`, and subject to diagnostic BCF2001. It is this static-expandability, not whether the attribute was attached, that separates the speed and trimming characteristics of part reuse.

**(D) Folding a static subtree. Input: a run of siblings written entirely in constants / Output: a single `AddMarkupContent` frame**

A region made up only of elements and text whose values are compile-time constants is serialized into a markup string and emitted as a single `AddMarkupContent` frame. The unit of folding is **a run, not a subtree** — a maximal sequence of consecutive foldable siblings.

```csharp
// Input (a design-time C# expression)
Div.Class("doc")[
    H1["BlazorCodeFirst"],
    Nav.Class("toc")[A.Href("#design")["Design"]],
    Span[$"Section {Index}"],
    P["Attributes are written before children."]]
```

```csharp
// Output (generated code): the runs before and after the dynamic Span each fold into one frame
__b.OpenElement(0, "div");
__b.AddAttribute(1, "class", "doc");
__b.AddMarkupContent(2, "<h1>BlazorCodeFirst</h1><nav class=\"toc\"><a href=\"#design\">Design</a></nav>");
__b.OpenElement(3, "span"); __b.AddContent(4, $"Section {Index}"); __b.CloseElement();
__b.AddMarkupContent(5, "<p>Attributes are written before children.</p>");
__b.CloseElement();
```

A folded run's frame width is 1, regardless of how many elements, attributes, and text nodes the run contains. Adjacent static siblings merge into one frame whenever nothing dynamic sits between them. This unit — a run rather than a subtree — is a correction #142's measurement revealed: the substance of the reduction comes from a run of static siblings, not from individual static subtrees.

A wrapper element stays an element frame only when it has a child that cannot be folded. In the example above, `div` stays because it has a dynamic `Span` as a child — a markup frame carries complete markup, so an opening tag cannot be folded together with a partial child list. Conversely, when the whole subtree is foldable, the root's opening tag lands in the same string too, and a fully static `Body` collapses the whole component into one `AddMarkupContent(0, …)` frame. The Razor compiler emits the same shape under the same condition (in the frame comparison #140 cites, the diff started at frame 2 rather than frame 0 because that example's `div` had a dynamic child).

Note that foldability is a strictly narrower notion than SSC (§2.3). SSC classifies whether a sequence number can be assigned statically, while folding requires the node's **value** to be a compile-time constant. `Span[$"Count: {Count}"]` belongs to SSC, but is not eligible for folding because its value is not constant.

The set of foldable tags is an allow-list: curated tags ∪ void tags ∪ custom element names, minus `pre` / `textarea` / `iframe`, whose text is interpreted differently from an ordinary element. Blazor escapes a value passed to `AddContent` but not one passed to `AddMarkupContent`, so escaping text and attribute values becomes the serializer's own responsibility. `Html.Raw` is excluded from folding: it is already one frame, so folding it alone gains nothing, and mixing it into an adjacent run is dangerous (an unbalanced string like `Raw("<i>")` would, when the whole run is parsed in one pass, pull the following siblings inside the `<i>`).

A value is also not folded when it cannot round-trip through markup. Four cases are excluded: carriage return (CR), NUL, an isolated surrogate, and a leading U+FEFF.

Even when constant, **a value that is not a string is not folded** (#158), because the compiler cannot know the culture the formatting will follow (when and where formatting happens is Appendix E.2). `3.5` reaches the DOM as `"3.5"` under `en-US` and as `"3,5"` under `de-DE`, and the compiler cannot know which. Folding it would bake one of them into the markup, so the same value would become a different string depending on whether its surroundings happen to be static. The cost of this exclusion is one missed opportunity to fold.

There are two exceptions. A **constant `null`** — string or otherwise — is omitted entirely, attribute and all, by `AddAttribute`, so the markup side matches by writing nothing either. A **constant `bool`** has nothing to format, so markup can express both outcomes exactly (`true` becomes `name=""`, `false` omits the attribute entirely). This is why `.Attr(name, bool)` is the one non-`string` spelling (`DESIGN.md` §4.1 and #158). The class channel folds by concatenation, so it too accepts only constant strings here.

Appendix E carries the shape of the mismatch behind each of the four exclusions, the measurements backing the two exceptions, and the character classes swept out once agreement was confirmed.

A `ForEach`'s content root is never folded, because `SetKey` cannot attach to a markup frame (see (B)). What guards this is simply whether the emitting side passes a key to the content root — there is no separate predicate, so the two can never disagree. A run that would absorb only a single frame is also not folded, since that would only change the shape without reducing anything.

**畳み込みは出力を変えずにコード経路を変えます。** 畳み込まれたマークアップと、要素経路が `HtmlEncoder` を通して書き出す出力は `&` `<` `>` `"` について同一です(それがDOM等価性の要件そのものなので当然そうなります)。したがって**出力に対するアサーションだけでは、畳み込み経路を通ったことを示せません**。畳み込みが静かに止まっても、そのテストは通り続けます。畳み込みを検査するテストが出力と併せて何を固定しなければならないかは、`CONTRIBUTING.md` §Conventions the code must uphold にあります。

**コンポーネントのスカラーパラメータは値を型付きで渡します。** `.Param` の値は `AddComponentParameter` の
`object?` 引数へ移植されるため、呼び出しサイトで持っていた目標型を失います。そこで発行側は、C#がその呼び出しで
解決した型引数へのキャストで値を包みます。型は解決済みの型引数から採り、選択されたプロパティの宣言型からは
採りません。値はその型引数へ既に変換済みであることをC#が保証しており、キャストが生成コードの中で束縛に失敗
し得ないためです。参照型は常にnullable として書き出します。キャストはnullについて何も主張せず、生成ファイルは
`#nullable enable` であるため、注釈を落とすと `null` と書かれた値でCS8600が出ます(#377、`Param_WithNullLiteralValue…`
が固定)。型が解決できない場合は書かず、今日と同じ綴りのまま発行します(`Component<T>()` の型引数と同じ規則)。

**コンポーネントの fragment スロット**: `RenderFragment` 型のパラメータは、スカラー値を持たずノードツリーを
持ちます。そのため `ComponentParameter`(スカラー)とは別チャンネル(`ComponentSlot` / `ComponentSlotNode`)へ
格納します。発行されるフレーム幅は `1 + Parameters.Length + Σ(1 + 内容のフレーム幅)` で、スロット1つが
`AddComponentParameter` 1回とその内容の幅を消費します。

ラムダ内部のシーケンス番号は外側の平坦なカウンタを継続し、独立したシーケンス空間を作りません。
スロットのフレームは呼び出し元ではなく**子コンポーネントのフレーム列**に属します。BlazorCodeFirst のジェネレータは
常に `AddComponentParameter(seq, "ChildContent", (RenderFragment)(...))` を発行する側です。
fragment を直接 invoke するかどうかは、渡し先コンポーネント(手書きでも Razor 生成でも)が `AddContent` に
渡すか自分で呼ぶかの問題です。前者は Blazor のリージョンが隔離しますが、後者はリージョンが張られず、
我々の番号がホスト自身のフレームと隣接します。0 から振り直すとホストの低い番号と衝突し、コンポーネントが
再生成されて状態が失われます(実測)。平坦継続が厳密に安全側です。これは Razor と同一の挙動で、
リージョンで包んでも解決しません(リージョンはホストのフレーム列における隣接関係を変えないため)。

**ジェネリックな fragment スロット**: `RenderFragment<TContext>` 型のパラメータは `.Template` で受けます。
名前が `ChildContent` の場合は角括弧でも受け、そちらはコンテキストを使わない綴りと同じものを発行します。
発行するのは、`TContext` を取る外側のラムダと `RenderTreeBuilder` を取る内側のラムダを重ねた2段の式です。
外側の引数は、コンテキストを使わない綴りでは破棄 `_`、コンテキストを読む綴りでは
`__bcf_context_<論理プレオーダー番号>` という生成名になります。内側は非ジェネリックのスロットと同一です。

```csharp
// 入力(設計時のC#式)
Component<Card>()
    .Param(c => c.Title, "t")
    .Template(c => c.HeaderTemplate, Span["heading"])
    .Template(c => c.RowTemplate, row => Span[$"Row {row}"])
```

```csharp
// 出力(生成コード): スカラーが先、スロットはソース順、seqは平坦に継続する
// (キャストの型名は表示の都合で短縮。実際は §2.2 のとおり global:: 修飾で書き出されます)
__b.OpenComponent<global::T.Card>(0);
__b.AddComponentParameter(1, "Title", (string?)("t"));
__b.AddComponentParameter(2, "HeaderTemplate", (RenderFragment<int>)((_) => (__builder) =>
{
    __builder.AddMarkupContent(3, "<span>heading</span>");
}));
__b.AddComponentParameter(4, "RowTemplate", (RenderFragment<int>)((__bcf_context_3) => (__builder) =>
{
    __builder.OpenElement(5, "span");
    __builder.AddContent(6, $"Row {__bcf_context_3}");
    __builder.CloseElement();
}));
__b.CloseComponent();
```

チャンネルの発行順は、スカラーのパラメータがソース順で先、続いてスロットがソース順です。スロット内容の
シーケンス番号は外側の平坦なカウンタをそのまま継続し、独立した空間を作りません(非ジェネリックのスロットと
同じ規則です)。上の例の `RowTemplate` が `__bcf_context_3` を名乗るのは論理プレオーダー番号が3だからで、
自身の `AddComponentParameter` のseq(4)とは別の数です。両者が一致する保証はありません。

コンテキストの名前は生成側が決め、作者の書いた識別子は生成コードに現れません。作者のラムダ引数は
`[ViewPart]` の引数と同じ**穴**としてテンプレートに記録され、展開時に生成名が差し込まれます。穴の位置は
解析時にパラメータの `ISymbol` から決まるため、同じ綴りの別物(同名のフィールド、内側のラムダが再宣言した
同名の変数)は書き換わりません。ただし `ISymbol` と `TextSpan` はこの解析呼び出しの内側に閉じ、テンプレートへ
渡るのは書き換え後の文字列だけです。ジェネレータのインクリメンタルモデルは不変・値等価なレコードと
プリミティブと文字列だけで構成する必要があり、シンボルやスパンを持ち込めばキャッシュの等価判定が壊れます。

逆向きの衝突も2つ塞いであります。作者が `__bcf_context_*` という名前を自分で宣言していれば
`__bcf_authored_context_*` へ改名し、生成引数が作者の非静的メンバーを覆い隠す位置では `this.` を補います。

**(E) Non-attribute frame decorations. Input: `.Key` / `.Ref` / `.RenderMode` / Output: a non-attribute frame placed after the attribute frames**

`.Key` (Razor's `@key`), `.Ref` (`@ref`), and `.RenderMode` (`@rendermode`) are never composed into the owning node's attributes. They take part in neither (A)'s folding nor (D)'s static folding, and each falls to its own dedicated `RenderTreeBuilder` call. There are three decorations but four calls they fall to (`.Ref` splits by receiver). The four differ from each other in kind, and that difference directly decides the emission rules.

| Spelling | Call | Sequence | Frame | Attaches to | `null` |
| --- | --- | --- | --- | --- | --- |
| `.Key` | `SetKey(object?)` | consumes none | does not stack (writes the open frame's key field) | element/component | early return |
| `.Ref` (element) | `AddElementReferenceCapture(int, Action<ElementReference>)` | **consumes 1** | stacks | element only | — |
| `.Ref` (component) | `AddComponentReferenceCapture(int, Action<object>)` | **consumes 1** | stacks | component only | — |
| `.RenderMode` | `AddComponentRenderMode(IComponentRenderMode?)` | consumes none | stacks | component only | early return |

The three that stack a frame must be placed **after every attribute, event, binding, and slot of their owning node has been emitted, and before the children**. `RenderTreeBuilder`'s `AssertCanAddAttribute` and `AssertCanAddComponentParameter` look at the kind of the immediately preceding non-attribute frame, and adding an attribute after a reference-capture or render-mode frame has been stacked throws `InvalidOperationException`. Because a component's slot is also stacked as `AddComponentParameter`, "after the parameters" means after both scalars and slots. `SetKey` alone stacks no frame and only rewrites the parent frame, so it sits outside this rule, and — like a `ForEach` key — is emitted immediately after `OpenElement` / `OpenComponent` ((B)).

On the element side there is one more reason it must come before the children: Blazor's diff reads an element's reference-capture frame as part of the attribute range, so a capture placed after the children falls outside that range. On the component side, the order between the two is render mode first, reference capture second — this is a choice, not a requirement, since the builder accepts either order; the only basis is that there is no reason to place a different-kind frame ahead of the walk that returns the first `ComponentRenderMode` frame when the renderer looks for the call site's mode.

```csharp
// Input (a design-time C# expression)
Div.Class("tab").Key(tab.Id).Ref(r => _tab = r)[Span[tab.Label]]
Component<Editor>().Param(c => c.Text, _text).RenderMode(_mode).Ref(c => _editor = c)
```

```csharp
// Output (generated code)
__b.OpenElement(k,   "div");
__b.SetKey(tab.Id);                                       // consumes no sequence
__b.AddAttribute(k+1, "class", "tab");
__b.AddElementReferenceCapture(k+2, r => _tab = r);        // after the attribute, before the children; consumes 1
__b.OpenElement(k+3, "span"); __b.AddContent(k+4, tab.Label); __b.CloseElement();
__b.CloseElement();

__b.OpenComponent<Editor>(m);
__b.AddComponentParameter(m+1, "Text", (string?)(_text));
__b.AddComponentRenderMode(_mode);                         // after the parameters, consumes no sequence
__b.AddComponentReferenceCapture(m+2, __value =>           // after the render mode; consumes 1
    ((System.Action<Editor>)(c => _editor = c))((Editor)__value));
__b.CloseComponent();
```

A cast is needed on the component-side capture because the framework takes `Action<object>` while the surface takes `Action<TComponent>`. It is the `ComponentView<TComponent>` side that knows the type, so the generated side writes the cast rather than making the author write it. The cast to a delegate is needed to invoke, in place, the lambda written in the argument position — the same shape a synchronous setter binding carries for the same reason.

`.Ref` is the only one that increases `FrameWidth`. `.Key` can still move an element's frame count, though: because `SetKey` cannot be expressed in markup, an element carrying `.Key` becomes unfoldable, and even one written entirely in constants is not folded into (D)'s single frame — it emits its own frame sequence instead. The same holds for `.Ref`. A component is already outside folding's scope, so `.RenderMode` moves neither. The rule at the end of (D) — that it is emission itself which determines width — still holds here, and no independent width calculation should be added for these three.

Writing the same frame decoration twice on the same node breaks all three in a way that fails to match what was written. `SetKey` overwrites the parent frame's key field, so only the one emitted later survives; `AddComponentRenderMode` goes the other way — `Renderer.FindCallerSpecifiedRenderMode` returns the first `ComponentRenderMode` frame, so only the one emitted first takes effect; and a reference capture stacks two frames, firing both Actions. None of these follows a priority the author wrote, so BCF3033 rejects it. The shape where a `ForEach`'s key collides with the content root's own `.Key` is a different reporting layer, and BCF3032 handles that separately.

---

## 3. Memory layout

### 3.1 The SSC path: zero intermediate representation

The SSC (and Transplantable) path's runtime shape is a straight-line sequence of `RenderTreeBuilder` instructions carrying static sequence constants. The generated form is the same as the Razor compiler's output: no intermediate object born of the UI description (an element tree, a builder, a `params` array) is ever created on the heap. The marker type `View` is an empty `readonly struct`, unreachable at run time.

This makes the SSC path's allocation profile equivalent to the equivalent Razor component's — a measured figure in `DESIGN.md` §7.1, not a prediction. What allocation remains comes only from Blazor itself: event-handler delegates/closures, `RenderTreeBuilder`'s internal frame array (reused), and temporary strings from interpolation (partially mitigated via the `ISpanFormattable` path).

### 3.2 The Opaque path: a `View` that wraps a fragment

Only on the Opaque path does `View` carry any substance. There, `View` is a lightweight handle that wraps a reference to a `RenderFragment`, and the heap allocation is confined to building that wrapped fragment — the same cost as hand-composing a `RenderFragment` (a measured figure in `DESIGN.md` §7.1, not a prediction).

```csharp
public readonly struct View
{
    internal readonly RenderFragment? Fragment;   // always null on the SSC path (unreachable)
    internal View(RenderFragment fragment) => Fragment = fragment;
}
```

`implicit operator View(RenderFragment?)` constructs this field. This is the only spelling under which `View` carries substance, and because every member of the design-time surface (`Html` / `ElementView` / `Decorations`) returns a default value, a `View` built from the surface never carries a fragment. This asymmetry is BCF3030's basis (Appendix A, 付録B.11).

Because the generated code sits in the user's own assembly, it cannot read the `internal` field. There is exactly one path that reads it, `BlazorCodeFirst.CompilerServices.ViewRuntime.FragmentOf`, positioned the same way Razor's generated code calls `Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers`. Emission is a single `AddContent(seq, RenderFragment?)` frame; no `OpenRegion` is written, since Blazor itself opens a region for the fragment in response to this call — the same behavior `RenderFragmentContentNode` relies on.

### 3.3 Folding a static subtree

For a subtree that does not depend on state (a fixed header, terms of service, and so on), the Source Generator detects nodes whose values are compile-time constants and serializes a contiguous range into one markup string. What remains at run time is a single `AddMarkupContent` call — no element, attribute, or text frame is emitted. Not only does value recomputation and reformatting never happen, the frame count itself goes down. §2.7(D) defines the unit and the conditions of folding.

For a part that was not folded, frame emission still happens every time, since Blazor's diff detection requires it. Across the whole component, the frame count splits: a constant number (one per run) for the static parts, and — as before — a number proportional to the node structure for the dynamic parts.

---

## 4. Event propagation and the concurrency model

### 4.1 Execution order and single-direction data flow

From a user action to the DOM update, execution proceeds in one direction, in this order:

1. **Event fires** (the browser)
2. **Dispatch**: dispatch to Blazor's `SynchronizationContext` completes
3. **State transition**: the update from `s_t` to `s_{t+1}`
4. **Frame-sequence generation**: `RenderView` runs, generating `r_{t+1}`
5. **Diff application**: `Δ(r_t, r_{t+1})` synchronizes the DOM

The point of this order comes down to one thing: state transition must precede frame-sequence generation (state transition → generation). This enforces single-direction data flow, meaning a state transition must never occur while `RenderView` is running. At the current source-level implementation, this corresponds to "no state mutation inside a design-time expression (`BodyComponentBase.Body` or `ChromeLayoutBase.Chrome`)", and a violation is diagnostic BCF3001. A deferred handler argument, such as a `Button`'s onClick lambda, is excluded, since it does not run during rendering and executes only after the event. Complete detection of a side effect reached through an arbitrary method call is not guaranteed (see §1.1's BCF3001 note). Equivalent verification for a `[ViewPart]` body is a candidate for future extension, and is not part of this initial contract.

### 4.2 Division of labor with Blazor's standard dispatch

Blazor already provides serialized dispatch onto the rendering thread via `SynchronizationContext` (and `ComponentBase.InvokeAsync`). BlazorCodeFirst does not replace this. What this library adds to the concurrency model is limited to the following two points.

First, static verification by the analyzer of §4.1's "state transition → frame-sequence generation" order (the Blazor standard has only a convention, with no enforcement mechanism). Second, `Interlocked`-based lock-free notification coalescing that merges multiple state-change notifications from an external thread into a single re-render:

```csharp
private int _renderPending; // 0 or 1

public void NotifyStateChanged()
{
    if (Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
    {
        _dispatcher.InvokeAsync(() =>
        {
            Volatile.Write(ref _renderPending, 0);
            StateHasChanged();
        });
    }
}
```

In a Wasm environment (effectively single-threaded today), the CAS always succeeds uncontended, so the overhead reduces to a single branch.

### 4.3 Runtime Async (net11.0, conditional)

On the net11.0 target, Runtime Async (runtime-native async) reduces async event handlers' state-machine overhead and flattens stack traces. No code change is needed on BlazorCodeFirst's side; the benefit comes from switching the TFM alone.

---

## 5. WebAssembly and AOT compilation compatibility

BlazorCodeFirst eliminates runtime metadata analysis and dynamic dispatch. Every parameter binding (including `Component<T>().Param(...)`) goes through a static setter the Source Generator generates. `Param`'s expression argument is parsed by the SG purely to generate that setter; no expression tree (`System.Linq.Expressions`) is ever compiled at run time. **There are zero places where the generated code calls `System.Reflection` / `System.Linq.Expressions`.** On the framework side the generated code calls into, there are two paths that pass through reflection, and both sit inside the boundary this section draws further down (the contract runs only as far as the code this design itself generates): `ComponentProperties.SetProperties` for `Component<T>().Param`, and, when binding an enum, `BindConverter.ParserDelegateCache` (which pulls out `ConvertToEnum` via `MakeGenericMethod`, #307). The latter was measured to survive trimming (`TrimmedOutputTests`, cost in Appendix E.2).

Furthermore, because both the design-time expression (`BodyComponentBase.Body` or `ChromeLayoutBase.Chrome`) and the design-time API are unreachable at run time, the IL trimmer can remove them wholesale. The design-time API here means every member of `Html` and `Decorations`, and every member of the design-time inert types `View` / `ComponentView<T>` / `ElementView` (Appendix A, BCF3014). The UI description's source code contributes nothing to binary size — a property a code-first approach that evaluates at run time cannot get. The design is verifiable, with `TrimMode=full` and `ILLinkTreatWarningsAsErrors=true` enabled, by scanning for MethodDef via `System.Reflection.Metadata`. The trim tests check this for both a component and a layout (a derived type's `Body`/`Chrome`, and the base's abstract getter).

Compared with an equivalent configuration that uses reflection-based binding, this is projected (predicted) to cut the AOT-compiled Wasm payload size by roughly 20-30%. This prediction is to be replaced with confirmed figures from benchmarks across three configurations: (a) BlazorCodeFirst, (b) reflection-binding, and (c) plain Razor. Against plain Razor, the expectation is near parity.

What BlazorCodeFirst's trimming/AOT-compatibility contract covers extends as far as the code it generates itself (a reflection-free `RenderView`, the design-time API that is unreachable at run time, and the `ComponentView` builder) being removed by trimming. For component embedding via `Component<T>().Param(...)`, at the stage where parameters are applied at run time, the framework's reflection-based `[Parameter]` binder (`ComponentProperties.SetProperties`) becomes reachable. This falls under the Blazor SDK's own trimming profile, and is not BlazorCodeFirst's responsibility. The trim-test harness (`tests/BlazorCodeFirst.TrimTestApp`) is a plain console app carrying no Blazor SDK profile, so this one framework-side `IL2072` surfaces on its own — a suppression scoped to `ComponentProperties.SetProperties` alone (`ILLink.LinkAttributes.xml`) is applied for exactly that reason.

`Component<T>()`'s type argument falls, as a literal, into the generated code's `OpenComponent<T>`, so it must already be resolved by the time BlazorCodeFirst's generator runs. Because source generators cannot see each other's output, a `.razor` component **in the same project** never satisfies this condition, and is reported as BCF3012. A `.razor` component in a referenced project or a NuGet package resolves normally, so this constraint is confined to within the same compilation; a hand-written C# component is always available.

---

## 6. .NET 11 conditional formal definition: the closed-world `ViewNode` (reference specification)

On the net11.0 target, C# 15's union types and the `closed` modifier are used to define the Source Generator's internal representation — the set of UI nodes — as a closed discriminated union:

```csharp
#if NET11_0_OR_GREATER
public closed union ViewNode
{
    TextNode(string Content, StyleSet Style);
    StackNode(Axis Axis, int Spacing, ViewNode[] Children);
    ButtonNode(string Label, ActionRef Handler, ButtonStyle Style);
    RegionNode(int Seq, KeyRef? Key, ViewNode Body);
    ComponentNode(TypeRef ComponentType, ParameterBag Parameters);
}
#endif
```

Closing the world lets the exhaustiveness of the compiler's internal visitors (frame emission, dependency analysis, diagnostics) be verified at compile time — a missed case becomes a compile error — and lets the type system guarantee `FrameWidth`'s (§2.2) totality.

> Note: as of the .NET 11 preview, union types leave some features (member providers, and so on) unimplemented, and this chapter is a reference specification to be formalized after GA. On the net10.0 target, the equivalent structure is approximated with a `sealed` class hierarchy plus an exhaustiveness analyzer.

---

## 7. Technical-fit specification summary

| Evaluation criterion       | Blazor (plain Razor)              | BlazorCodeFirst (this system)                                 | Notes                                      |
| -------------------------- | --------------------------------- | ------------------------------------------------------------ | ----------------------------------------- |
| Authoring paradigm         | Markup-first (HTML + C#)          | Code-first (pure C#)                                          | The same family of authoring experience as SwiftUI/Compose |
| Type safety (Style/Layout) | Low (depends on string CSS/class names) | Fully type-safe (compile-time verified)                 | Driven by IDE IntelliSense                |
| Compilation approach       | Razor compiler (markup → C#)      | Source Generator (C# expression → C#)                         | Same output shape                          |
| Sequence-number management | Static assignment by the compiler | Static assignment by the SG (SSC) + region isolation (Transplantable/Opaque) | The author need not think about sequence control at all |
| Runtime intermediate representation | None                      | None (SSC path) / a fragment-wrapping `View` (Opaque path only) | Zero heap allocation born of the UI description |
| GC allocation               | Baseline                          | Equivalent (measured)                                          | Measured in `DESIGN.md` §7.1. Static-subtree folding (§2.7(D)) makes the frame sequences match too |
| Render time                 | Baseline                          | Equivalent (not published)                                     | Measured, but `DESIGN.md` §7.1 does not publish a figure because the variance is machine-dependent |
| AOT / Wasm compatibility    | Compatible                        | Fully compatible (zero reflection dependency, UI-description code is trimmed away) | 20-30% reduction versus a reflection-based configuration (predicted) |
| Hot Reload                  | Already integrated into tooling   | The EnC standard path (method-body swap + `MetadataUpdateHandler`) | Post-edit semantics are identical to Razor's (§2.6) |
| Supported TFMs              | —                                  | net10.0 (baseline) / net11.0 (union-type internal representation, etc.) | Multi-targeting that prioritizes LTS |

---

## 付録A: Diagnostic Catalog

### A.0 A constraint on the reporting path: a diagnostic that explains a compile error cannot be reported by an analyzer

csc does not run the analyzer driver against a compilation that contains a declaration-level error (CS0534, CS0246, CS0234, and so on). This is standard Roslyn behavior, because analyzers presume a valid symbol model. The Source Generator driver has no such gate, so diagnostics the generator reports coexist with declaration errors in the output.

This gives the rule that decides where a diagnostic must be implemented.

> **If a diagnostic's role is to name the cause of a compile error the author cannot resolve alone, the Source Generator must report it.** Implemented as an analyzer, the very condition that should fire the diagnostic is what stops the analyzer driver, making it unreachable in principle.

BCF1001 violated this rule (#76). A missing `partial` means `RenderView` is not generated, which always produces the declaration-level error CS0534, so BCF1001 as an analyzer could never be reported in a real build — the condition that should have been diagnosed was suppressing the diagnostic itself. BCF1001 has been moved to generator reporting. For the same reason BCF1003 / BCF1005 were generator-reported from the start, and are emitted alongside CS0534.

A side effect follows: **in a compilation that contains even one declaration error, every analyzer diagnostic for that project disappears, not only BlazorCodeFirst's own (this includes CA/IDE rules too)**. This is not a property specific to BlazorCodeFirst, but a non-partial component is the easiest way to fall into this pitfall, which is also why reporting BCF1001 immediately from the generator has value.

The current reporting path is: BCF3001 by `RenderMutationAnalyzer`, BCF3029 by `InertSurfaceAnalyzer`, and everything else by `BlazorCodeFirstGenerator`. The two analyzers can be analyzers precisely because the shape that fires them compiles. BCF3001 is a design-time expression that contains a state mutation, and BCF3029 is a design-time API written where nothing reads it; both pass type checking, so the analyzer driver runs. When adding a new diagnostic, decide first whether the shape that fires it compiles.

This section is not a documentation-only promise; a test pins it. `tests/BlazorCodeFirst.DiagnosticTests` builds each project under `tests/diagnostic-fixtures` with a real MSBuild build and checks, from the SARIF log, which diagnostic was reported where. Including the same CA1050-violating type in every fixture pins two facts at once: that analyzer diagnostics disappear from a compilation with a declaration error, and that they are reported from a compilation without one. Every descriptor in `DiagnosticDescriptors` must either be covered at this layer or be on the exclusion list with a reason.

The table in the next section is verified by the same test project. `DiagnosticTableTests` reads the A.1 table and cross-checks it against `DiagnosticDescriptors` in both directions. It fails if a descriptor exists with no matching row, and it also fails if a row exists with no matching descriptor, unless the reason it is specified ahead of implementation is recorded in `DiagnosticExpectations.DocumentedWithoutDescriptor`. That registration is swapped out for the descriptor at implementation time, and a missed swap is caught by a separate test as "an exception that lost its reason." The Kind column is also checked against the descriptor's `DefaultSeverity`, so changing a diagnostic's severity also means changing the table (a row with no descriptor is excluded from this check).

### A.1 Diagnostic Table

| ID     | Kind    | Description                                                                             |
| ------ | ------- | ------------------------------------------------------------------------------------- |
| BCF1001 | Error   | A class that declares an override of a design-time expression (`BodyComponentBase.Body` or `ChromeLayoutBase.Chrome`) is not declared `partial` (`RenderView` cannot be generated into the same class). Excluded: a class that merely inherits a BlazorCodeFirst base without declaring the override (an intermediate abstract base, a leaf whose base already declares it, a re-abstraction), and a class that hand-writes `RenderView` (no `partial` is needed because nothing is generated). BCF1005 takes priority for a nested class, because adding `partial` would not resolve it. Reported by the generator (reason: A.0)  |
| BCF1002 | Error   | Static expansion of `[ViewPart]` does not hold. At the declaration site, this is reported when the shape fails to satisfy the static-expansion contract the Source Generator supports. The shapes that fail it: an extension member (`DESIGN.md` §4.3, #203); non-static; generic; contained in a generic type; a body that does not reach a single `return` (a second `return`, a native control construct, or a local with a generator-reserved name — the accepted shape is the same as §2.3 Transplantable, and there is one implementation that reads it); a body that returns neither `View` nor `SlotView`; a `params`, by-reference, or `ElementView`-typed parameter; a `View`-typed parameter on a declaration that does not return `SlotView`; a parameter of a type the generated code cannot name; or a body that is not statically sequenceable. At the call site, it is reported under three conditions: (1) the method's source declaration is not in the current compilation (metadata only) — definitions are collected from the current compilation's syntax via `ForAttributeWithMetadataName`, and IL carries no body syntax, so a `[ViewPart]` in a referenced project or NuGet package always falls here; (2) a recursive expansion forms a cycle; (3) the expansion site cannot reach a `private` / `protected` member the body references. As a third position, the same check also reads the body of the component's own design-time expression (`Body` / `Chrome`). A reference to syntax the generated code cannot name (a local function, a local declared where the generated code's scope does not enclose it, a range variable, a label) falls here, and where the enclosing position is defined is in §2.3. The message's subject differs by position: `[ViewPart]` reads `ViewPart method 'X'`, and a design-time expression reads `The Body design-time expression of 'C'` — the latter is not a method, so the former's wording would send the author looking for something not in their own file (#361) |
| BCF1003 | Error   | A design-time expression (`Body` / `Chrome`) cannot be classified into a statically sequenceable subset. Now that the Opaque and Transplantable paths exist, a `View`-returning call and a block-bodied `ForEach` content are not this diagnostic — BCF2001 / BCF3030 and BCF3004 see them respectively. What remains is an expression that is neither a call nor design-time syntax; reading back a stored `View` is such a shape. It is also where an `If` branch that declares a local with a generator-reserved name (the `__bcf_` prefix and `__builder`) falls: a branch is transplanted under the author's own name, so it rejects reserved names for the same reason as a getter (#389). The same applies where the body of a spliced projection (`.. source.Select(item => …)`) declares the same kind of name: a projection is transplanted under the author's own name into the inside of the folded loop, and the rejection falls to the same number as the other spliced shapes (#413) |
| BCF1004 | Error   | An override of a design-time expression (`Body` / `Chrome`) declares a getter the generator cannot translate. The accepted shape is a getter that reaches one `return`; it may be preceded by local declaration statements and expression statements, which are transplanted ahead of frame emission (§2.3 Transplantable). Four shapes therefore remain: a second `return`; a native `if` / `foreach` / `switch`; a local with a generator-reserved name (the `__bcf_` prefix and `__builder`); and an auto-property with no body. The first three are the same shapes `ForEach`'s `content` rejects for the same reason, and there is one implementation that reads them. Rewrite it, or hand-write `RenderView`. A re-abstraction (`abstract override`) is excluded, as is a partial property with no implementing part (CS9248 names the cause) |
| BCF1005 | Error   | A nested class declares a design-time expression. Generated code cannot reproduce the chain of enclosing type declarations, so it must be moved to a top-level type |
| BCF2001 | Info    | Detects a `View`-returning call that cannot be statically expanded. It renders through the `RenderFragment` the returned `View` wraps, and loses static-diff optimization for that region; correctness is unaffected. This applies when the call target's source declaration is not in the current compilation, or is but its body does not reference the design-time surface — BCF3030 stops the case where it does. `RenderFragmentContentNode`, which emits `AddContent(seq, RenderFragment?)`, is the spec's Opaque path, but is not covered here because the written side is already a `RenderFragment` and never reaches call classification. #32's `ComponentSlot` is also excluded, as a complete SSC path made only of `AddComponentParameter` and a statically numbered lambda. **Unmeasurable residue**: if a `View`-returning method in a referenced assembly was built on the design-time surface, that `View` is empty at runtime and renders nothing, but with no source declaration this cannot be determined, and this diagnostic is emitted instead. `DESIGN.md` §4.3 steers reuse across an assembly boundary toward components |
| BCF3001 | Error   | In the current implementation, a state mutation inside the body of a design-time expression (`BodyComponentBase.Body` or `ChromeLayoutBase.Chrome`) — a single-direction data-flow violation. Initial detection scope: a direct write to a component instance member (assignment, compound assignment, increment, decrement). Excluded inside a deferred handler argument (including a nested lambda). What is excluded is a handler argument of an event decoration (an event shorthand such as `.OnClick`, and `.On`), a `.Bind` setter argument, `.Ref`'s capture behavior, and the value of a component's `.Param` — this follows from `KnownSymbols`'s classification itself, not an enumeration of names. The `.Param` value is excluded because it is the child, not frame generation, that calls the delegate written there (#385). A handler wrapped in `EventCallback.Factory.Create` is likewise excluded, answered not by classification but by `KnownSymbols.IsEventCallbackFactoryMethod`. A `.Bind` getter argument is not excluded, because it is evaluated during frame generation. Nor are the selectors of `.Bind` and `.Param`, since they only name a parameter and carry no value. Complete detection of arbitrary side effects is not guaranteed. Applying this to a `[ViewPart]` body is a candidate for future extension |
| BCF3002 | Warning | A `ForEach`'s `key` selector may not guarantee element identity (e.g. an index-based key). Not reported when `key: null` is written, since there is no key to question. This diagnostic warns about a key that was written, not about omitting one (#172) |
| BCF3003 | Error   | A keyed `ForEach`'s `content` does not have a single element/component as its root, so the key cannot be applied (a bare `if`/`ForEach` whose root becomes a region, `Fragment`, `Raw`, and so on). Wrap the inside in a container element (e.g. `Div[...]`). A `ForEach` with `key: null`, and its spread sugar, are excluded because they do not emit `SetKey` — there, `Fragment` / `Raw` / a bare `If` may stand at the root. This rule exists because `SetKey` can only attach to an element's or component's frame; with no key to attach, the constraint has no basis (#172) |
| BCF3004 | Error   | A `ForEach`'s `key` is neither an inline expression lambda nor a written `null`, or `content` is not a shape the generator accepts. `key`'s body must be an expression because it is transplanted into `SetKey`; since the transplant target is `RenderView`'s own traversal, a body that declares a local with a generator-reserved name is also excluded, answered by the same traversal as `content` (#413). The absence of `key` is read as syntax: because the generator transplants the written body rather than a runtime value, passing a variable that holds `null` is not an inline expression lambda and stays this diagnostic (#172). `content` accepts three shapes: an inline expression lambda; a block-bodied lambda that reaches exactly one trailing `return` and otherwise contains only local declaration statements and expression statements (§2.3 Transplantable); and a single-parameter method group that returns `View`. A method group is reread as a call passing one iteration variable, and falls to the same three-way branch as any other call (static expansion / BCF3030 / Opaque). Multiple `return`s and native control constructs are each excluded, since each needs its own sequence space. A shape that declares a local with a generator-reserved name (the `__bcf_` prefix and `__builder`) is also excluded regardless of whether the lambda body is an expression or a block, because both are transplanted under the author's own name into the inside of the loop, answered by the same traversal as a getter (#389). A constructed delegate (`new Func<T, View>(M)`) is also excluded, since the call site cannot name its target |
| BCF3005 | Error   | A `Component<T>()` parameter binding (`.Param` / `.Template` / `.Bind`) selector is not a simple property selection (`c => c.Prop`) — e.g. a cast, a method call, a captured variable's member |
| BCF3006 | Error   | The target of a `Component<T>()` parameter binding (`.Param` / `.Template` / `.Bind`) is not a settable `[Parameter]` property (rejected at compile time to prevent a runtime throw) |
| BCF3007 | Error   | A `Component<T>()` chain binds the same property more than once. This counts across all of `.Param` / `.Template` / `.Bind` and the bracket's child content (Blazor applies only the last value, so a duplicate is rejected at compile time) |
| BCF3008 | Error   | A decoration (`.Class`/`.Attr`/a typed attribute shortcut/`.OnClick`/`.On`) is written on something other than a node that opens a single element (an element helper / `Element`). A decoration is an extension of `ElementView`. So when the receiver is `View`/`ComponentView<T>` (`If`/`ForEach`/`Fragment`/`Raw`/a `[ViewPart]` result/`Component`, or an element that has already been given its children), overload resolution against `Decorations` fails. There is an exception for a `ComponentView<T>` receiver: `.Key` and `.RenderMode` are declared by `ComponentView<T>` itself rather than by `Decorations`, so they resolve and are not covered by this diagnostic (§2.7(E)). `Component<T>()[…]` returns `View`, so `.Key` on a component that has already been given its children is rejected here just like any other decoration. A `RenderFragment` passed in from outside is also accepted as a receiver: it converts implicitly to `View`, but an extension method's receiver only accepts identity/reference/boxing conversion and not a user-defined conversion, so resolution fails the same way, and the author's mistake is identical to decorating `Fragment`/`Raw`. This walks a design-time expression that failed translation and reports this failed chain when it finds one (because the CS1929 the type system would raise never reaches the author, cut off at the declaration stage — §2.2) |
| BCF3009 | Error   | `Element`'s tag argument is not a compile-time constant string (for declarativeness and predictability), or is not a spelling of a tag name. The spelling rule: starts with an ASCII letter, followed by ASCII alphanumerics, `-`, `_`, or `.` (`KnownSymbols.IsValidTagName`). A spelling an element cannot bear its own name under makes the two render paths emit different things, and neither is what was written: prerendering writes it out as markup that the HTML parser reinterprets, and interactive passes it to `createElement`, taking down the whole circuit (#394). This check needs no content-model table, so it sits inside §4.1's boundary. The option of giving a runtime tag a path was rejected in 付録B.14 |
| BCF3010 | Error   | An attribute or event is bound more than once on the same element (a duplicate within the attribute channel is last-wins, killing the earlier one; a same-named binding spanning the attribute channel and the event channel leaves both alive and firing twice — rejected in both cases because neither matches what was written). The one exception is `class`, which folds; coexistence with a `.Bind("class", …)` that does not fit that exception is BCF3024's concern |
| BCF3011 | Error   | `.Attr`'s name, `.On`'s event name, or `.Bind`'s attribute name and event name is not a non-empty compile-time constant string (a precondition for declarativeness, typo checking, class-folding determination, and duplicate detection). The option of giving a runtime name and attribute spread a path was rejected in 付録B.14 |
| BCF3012 | Error   | `Component<T>()`'s type argument cannot be resolved at generator run time. A `.razor` component in the same project is always in this state, because the Razor compiler is itself a source generator and the two cannot see each other's output. A `.razor` in a referenced project/NuGet package, and a hand-written C# component, resolve normally. For a typo or a missing `using`, CS0246 is also reported at the same position |
| BCF3013 | Error   | `Component<T>()[…]` is given child content, but `T` has no `ChildContent` (a settable `[Parameter]` of type `RenderFragment` or `RenderFragment<TContext>`) that can receive it. In the generic case the bracket binds together with an outer lambda that discards the context, so that case is excluded. A generic fragment named something other than `ChildContent` cannot bind through the bracket, so giving a bracket to a type that has only that name produces this diagnostic |
| BCF3014 | Error   | A design-time inert type (`View` / `ComponentView<T>` / `ElementView` / `SlotView`) was passed in a generic `.Param`'s value position |
| BCF3015 | Error   | A value expression inside a body contains an unresolved type reference that cannot be safely transplanted into generated code |
| BCF3016 | Error   | A void element is given children. The target is the HTML Living Standard's 13 void elements (`area` / `base` / `br` / `col` / `embed` / `hr` / `img` / `input` / `link` / `meta` / `source` / `track` / `wbr`). Covers both a curated helper and an `Element` given its tag as a non-empty constant. Static SSR emits a closing tag and the HTML parser pushes the children out to siblings, so prerendering and interactive rendering produce different DOMs (reason and measurement: `DESIGN.md` §4.1). Judged by a unary predicate over the element tag, so the same kind of break determined by (parent, child) is excluded. Unknown tags and custom elements are also excluded |
| BCF3017 | Error   | `.Bind`'s getter is not an inline lambda with a body expression (e.g. a block-bodied lambda, a method group). The getter's body expression is transplanted into both the attribute value and `CreateBinder`'s current value, so it must be extractable as an expression. The setter side carries no such constraint, since it is only passed to `EventCallback` and its body is never extracted |
| BCF3018 | Error   | In a `.Bind` written with only a getter, the getter's body is not assignable. Allowed are member access (`_name` / `_form.Name` / `Model.Items[0].Title`) and element access (`_dict["k"]`), where the target has a callable setter. Rejected: a call or an operation (`() => _name.ToUpper()`), a get-only property, and a `readonly` field. Having a setter and the derived assignment being able to call it are different questions, so an `init` accessor, and a setter the generation target cannot reach, are also rejected. The component's own `Body` is emitted into a partial of the same class, so reachability is judged against that component type; `{ get; private set; }` is allowed when the component itself declared it, and rejected when another type did. Because a `[ViewPart]`'s expansion site is not yet decided at this point, its body instead records the requirement of access to the setter, and BCF1002 rejects an unreachable expansion site. Direct assignment to a local variable, a parameter, or a `ForEach` iteration variable itself is also rejected (`Body` is a property getter and a local dies with every render, so a write-back does not survive to the next render). A **member** of the iteration variable (`o.Title`) rewrites the original element and is allowed. This steers the author toward a form that names the setter explicitly. Fires on both an element and a component, and even for the same shape the argument count differs by side (3 and 4 for an element, 2 and 3 for a component), so argument count is not used to tell the shapes apart |
| BCF3019 | Error   | A `.Bind` / `.On` event name does not start with `on`. A Blazor event attribute name always starts with `on`; anything else is silently added as a plain attribute and its handler never fires. `.Bind` takes two adjacent strings, an attribute name and an event name, so this check stops a mix-up between them |
| BCF3020 | Error   | `T` has no `{name}Changed` parameter matching the target of a `ComponentView<T>.Bind`, or it is not `EventCallback<TValue>`. Unlike the element side, the component side derives the name, and it can because a type symbol confirms it; this diagnostic rejects when `{name}Changed` does not exist or its type does not match. The other half, `{name}Expression`, is not covered by this diagnostic: it is emitted only when declared with a matching type, and silently omitted otherwise (the same behavior as Razor — emitting it unconditionally for a type that does not declare it would fail binding itself) |
| BCF3022 | Error   | `Component<T>().Template`'s content, in the contextual overload (the shape that takes `Func<TContext, View>`), is not an inline expression lambda (e.g. a method group, an anonymous method, a block-bodied lambda). The generator needs both the sequenced expression and the parameter symbol the generated context variable is assigned to, so a shape that cannot yield either is rejected. The position is the whole `content` argument, because the shape of the argument itself is what needs rewriting. A lambda with zero, or two or more, parameters is excluded from this rule: it cannot convert to `Func<TContext, View>`, and C# rejects it first. A `content` body that declares a local with a generator-reserved name (the `__bcf_` prefix and `__builder`) is also rejected, because the body is transplanted under the author's own name into the inside of the generated fragment, answered by the same traversal as the other transplant sites. Even a name the context-variable rename (the rule that rewrites `__bcf_context_<number>` to `__bcf_authored_context_…`) could rescue is rejected here, because the traversal is not split by position, and a name the rename cannot rescue is broken by measurement. A shape that declares `__bcf_item_0`, the same name as the enclosing `ForEach`'s iteration variable, becomes CS0841 (#413). This places BCF3004's same constraint on a template rather than a `ForEach`. The number skips BCF3021 because BCF3021 has been withdrawn (付録B.5) and is not reused |
| BCF3023 | Error   | A decoration that folds into the class channel (`.Class` / `.Attr("class", …)`) has a value that, in the resolved overload, is not `string`. `class` folds into the class channel, and this channel concatenates decorations into one value as text, so `string` is the only value type it can concatenate. The condition is this channel's own requirement, not an enumeration of overloads that fail to meet it (`ClassChannel.Admit` asks whether it is `string` and rejects anything else, #193). Today only two spellings reach this rule: the `bool` overload (#158), and `.Attr("class")` written with no value — a consequence of `.Attr` having only those two value types. If a non-`string` overload is added later (#171, #178), it stops at the same gate without touching the analyzer, so the message does not assume which type reaches the rule and instead names the type it found (#223). For `bool`, the meaning is not only undefined there — it is not even settled to one thing. If the element has exactly one class decoration, the channel emits the value as-is, so `AddAttribute(int, string, bool)` binds, and `true` becomes `class=""` — the class list erased. With two or more, they are concatenated with `+`, so the same `true` is stringified and becomes `class="a True"` (both measured, #159). The same spelling translates two different ways depending on how many other places in the chain carry the name — a translation break born of the generator's own folding. This applies only when the name is `class`; `.Attr("disabled", flag)` is the `bool` overload's intended use and is excluded. The position is the value argument, since that is what needs rewriting (write a conditional class as a conditional expression on the string side, `.Class(active ? "on" : null)`). The value-less spelling `.Attr("class")` also reaches this same rule: a bare spelling denotes presence, but the channel concatenates as text, and presence has nothing to concatenate (#178). Since there is no value argument to point at, the position in this case is the decoration name, and the message names the spelling itself rather than a synthesized `bool` — naming a value type the author never wrote would describe the compiler's own procedure rather than the author's code |
| BCF3024 | Error   | A class-channel decoration (`.Class` / `.Attr("class", …)`) and a `.Bind` whose attribute name is `class` are on the same element. The channel folds however many decorations it has into one frame, but `.Bind` does not join it and emits its own frame from the binding loop, so the element is emitted with two `class` attributes. This is a duplicate that reaches the one name BCF3010 lets through, and that exception was bought by the channel's folding, so the question here is asked of the channel, not the name. Because `.Bind`, the third spelling that reaches `class`, is the only one that does not fold, it collides with every other decoration of this name and with nothing else (#188). Which frame survives is not specified: prerendered markup resolves it first-wins by the HTML parser, and interactive rendering resolves it last-write-wins into the DOM, so there is no single answer. What the report needs is not that fact but that there is no way to write this that reads as wanting both frames. The position is the decoration name written second, pointing at the decoration the check runs on, as with BCF3010 |
| BCF3025 | Error   | `Slot` is written inside a declaration that does not receive caller content. Or, a `[ViewPart]` declared to take content (return type `SlotView`) writes `Slot` a number of times other than once. `Slot` marks where the content the caller gave through brackets is placed, so it has no meaning where there is no content to place. A component's `Body`/`Chrome` does not receive brackets, and a `[ViewPart]` that returns `View` is called without brackets. Zero occurrences discards content the caller was obligated to pass; two occurrences emit twice from one bracket — neither matches what was written. The position is `Slot` itself for a misplacement, and the declaration's identifier for a count error, each pointing at what the author must fix. This is the only diagnostic this surface needed to introduce new. A forgotten bracket (`Div[Card("x")]`), a decoration (`Card("t").Class("x")`), and the positional-argument spelling #176 rejected (`Card("t", P["x"])`) are all rejected by C# first, because `SlotView` has no conversion to `View` — the same mechanism by which `Div["x"].Class("y")` is CS1929 (#34, #176) |
| BCF3026 | Error   | `BlazorCodeFirst.Decorations` does not declare the name written in a decoration position. The receiver is a node that opens an element (`ElementView`), and only the name is broken. This differs from BCF3008 in that the question is about the name, not the receiver; the two are exclusive by the truth of `KnownSymbols.DeclaresDecorationNamed`, so the same walk classifies both. Two shapes are covered: a spelling that does not bind (`Div.Clas("card")`), where the CS1061 C# raises never reaches the author, cut off at the declaration stage (A.0); and a user-declared extension method that takes `ElementView` and returns `ElementView`, which does bind, so there is no C# error at all and only BCF1003 remained. A declaration whose return type is `View` is excluded — it is a wrapping shape rather than a decoration, so BCF1003 still applies. The position is the decoration name, because that is what needs rewriting |
| BCF3027 | Error   | At a position where an element is written as a simple name, what arrives is something declared outside `BlazorCodeFirst.Html`. `using static BlazorCodeFirst.Html;` brings the curated helpers into simple-name scope, but a closer declaration wins that lookup. What can arrive falls into four cases. For a member: if its type is indexable, the expression stays valid C# and becomes an indexer call on that member instead of an element (`Div[Data["Heading"]]` against `string Data` passes `"Heading"` to `Data`'s character indexer). For a type, a namespace, or a method, binding itself fails; C# raises CS1503 / CS0119 / CS0118 / CS0021 respectively, but each is an error at the body-binding stage, so it never reaches the author under A.0's cutoff, leaving only BCF1003's "syntax that cannot be statically analyzed." #127 excluded types on the grounds that C#'s `CS0119: 'Table' is a type, which is not valid in the given context` already names the shadowing declaration, but that premise was never measured and was wrong (#266). To the author these four shapes are one mistake (a simple name reached a declaration closer than `Html`) with one fix (`Html.<name>`), so this is one number, and what took the name is carried in the message's argument (the same shape as BCF1002). An ambiguous lookup with two or more candidates is excluded, since it cannot say which declaration won, and the helper itself may be among the candidates. #99, which widened the curated helpers from 22 to 100, did not create this break but moved its frequency from rare to routine. `Code`, `Data`, `Label`, `Summary`, `Source`, `Input`, `Option`, `Form`, and `Select` are all ordinary Blazor parameter names. The position is the shadowed receiver's identifier, because that is where `Html.Data` narrows to. The shadowing declaration is not attached as an additional location (none of the descriptors carry one) |
| BCF3028 | Error   | An event handler's argument type is not the type that event delivers. Two shapes are handled by one descriptor, with the reason carried in the message's argument (the same shape as BCF1002). To the author this is one mistake — the argument type was wrong for the event — with one fix, and whether C# could bind it is a distinction the author never drew, so it does not split the number. (1) A mismatch that binds (`.On("onclick", (KeyboardEventArgs e) => …)`) is reported from the successful decoration arm; both sides are already in hand. BCF3011 already requires the event name to be a constant, and the argument type is the type argument C# resolved before the generator looks at the expression — the inside of the lambda is not examined. (2) A type that violates `where TArgs : System.EventArgs` (`.On("onclick", (int x) => …)`) does not bind, so this is reported from the failure-path walk; the position and the reason are the same as BCF3008 and A.0. The CS0311 C# raises never reaches the author, cut off at the declaration stage, and measurement found only CS0534 and BCF1003 remaining. The test is assignability, not equality: `EventCallback<TArgs>` receives an event's argument object by casting it to `TArgs`. A base type is accepted; a sibling type is not (`.On("onclick", (EventArgs e) => …)` is valid). The correspondence table is read from two sources: `[EventHandler]` attached under `Microsoft.AspNetCore.Components.Web.EventHandlers`, and a type in the current compilation carrying the same attribute (the registration path for a custom event). The former is read first, so the latter never overwrites the same name. An event with no `[EventHandler]` correspondence is not reported, because this surface's tag is a string and there is nothing else to check it against. The gatekeeper differs by source: the framework's table requires `Microsoft.AspNetCore.Components.Web`, so a compilation that does not reference it leaves the table empty and a name only the framework registers is silently skipped; a registration in the current compilation requires only the `[EventHandler]` attribute, which lives in `Microsoft.AspNetCore.Components`, so it is read regardless of whether `Components.Web` is present (#396). A referenced assembly is not scanned (#155, because its cost cannot be estimated), and a registration placed there is not read — recorded as residue. The position is the handler argument, because the argument type written there is what needs rewriting. Razor carries an equivalent check from the same `[EventHandler]`, so the absence of this check means falling short of Razor on this one point (`DESIGN.md` §4.1) |
| BCF3029 | Error   | A design-time API expression is written outside the design-time expression that reads it. The design-time API is the set §2.1 and §5 list: every member of `Html` and `Decorations`, every member of the inert types `View` / `ElementView` / `SlotView` / `ComponentView<T>`, and `[ViewPart]` methods. An inert type's value is empty and the generator only reads its expression, so at this position no output is produced and no event handler is wired. Type checking passes, it looks as though something was assembled, and the only symptom is that nothing renders. Two conditions fire it: (1) the innermost declaration enclosing the expression (a method, property, accessor, local function, or lambda) does not return an inert type; (2) the value is not assigned to a field or property of an inert type. (1) excludes `Body` / `Chrome` / `[ViewPart]` with no positional allowlist: all three return an inert type, as do `If` / `ForEach`'s content lambdas, so introducing a new position that returns an inert type adds nothing to write down — having no enumeration is the point here, and the cost of making the check's host set a human enumeration is recorded in `FailurePathScanners`'s remarks (#100). (2) excludes caching into a `View`-typed field: §2.3 classifies calls, not storage, so a stored shape is simply unreserved today, and closing a door the design may want to open later with an Error is the wrong tool. An initializer is treated the same as an assignment; which of the two was written is not a distinction the author drew — an initializer has no return type of an enclosing declaration for (1) to reach. What is judged is the assignment target's type, not the value's type: a field typed `object` can receive it through boxing, but nobody can read a `View` back out of it, so that is not the storage opened here. A local is not excluded: a local dies with its declaration, and if it is returned or captured, (1) has already excluded that case. The author's own `View`-returning declaration is excluded — that is the Opaque spelling `DESIGN.md` §5.3 preserves, and 付録B.11(b) rejected removing it. What answers a forgotten `[ViewPart]` is BCF3030, which looks at the call site rather than the declaration (#260, 付録B.11). This row excludes only the declaration side; the expression that calls that declaration is BCF3030's concern. One report per written chain: `Html.Div.Class("card").OnClick(DoThing)[Html.Span["hello"]]` contains five references to the design-time API but one mistake. The position is the whole of the outermost design-time expression, because what is wrong is the expression's position, not its contents (the same placement as BCF3014). Hosted by `InertSurfaceAnalyzer`. This shape compiles, so A.0's prohibition does not apply, and BCF3001 is the precedent. Placing it in a type separate from BCF3001 is because BCF3001 looks only inside a design-time expression while this one looks only outside it; putting two range checks facing opposite directions in one type would make a later change unreadable as to which condition it touched. Registration is not for the whole syntax but for two operation kinds, `Invocation` and `PropertyReference` — the first conjunct is a type test, not the name-based prefilter #68 had planned. #68 required deciding this choice only after measuring it, and the measurement showed the question itself had been framed wrong: an analyzer that registers the same two kinds and does nothing, and one that registers the whole syntax and does nothing, land at the same cost. In other words the shape of registration is not where the cost lives — what the prefilter was trying to narrow was the free side. The cost comes from the callback, where the order of the conjunction is what matters. The numbers and the method are in #68 and are not placed in this table or the analyzer. The time is machine-dependent, for the same reason `DESIGN.md` §7.1 does not publish a time |
| BCF3030 | Error   | A call to a non-`[ViewPart]` method that returns `View`, where the call target's body references the design-time surface. The design-time surface is inert, and the only path that puts substance into a `View` is `implicit operator View(RenderFragment?)`, so a `View` built from the surface is always its default value at run time — the call renders nothing. C#'s type checking passes; the only symptom is the absence of output. Where BCF3029 looks at "a design-time expression written where nothing reads it" from the declaration side, this looks at the same break from the call side. The design-time surface it targets is the same set BCF3029's row defines, and the judgment draws on the same `KnownSymbols` classification. What decides it is only whether the body references the design-time surface; BCF1002's static-expansion contract does not run at the call site. A breakdown of the contract violation is named by BCF1002 at the declaration position, after the author attaches the attribute. There are two fixes: attach `[ViewPart]` for a static method; for an instance method, which cannot be `[ViewPart]` (BCF1002), make it a component instead. This applies only to a call to an ordinary method that returns `View`. `ElementView` and `ComponentView<T>` cannot in principle hold a fragment, since their conversion to `View` returns a default value, so they never reach this path and stay BCF1003. A call whose source declaration is not in the current compilation is also excluded — BCF2001 sees that instead. The position is the whole call expression, because that is what needs rewriting. 付録B.11 records how this diagnostic was revised into its current form |
| BCF3031 | Error   | `format` is written on a `.Bind`, but the framework declares no format-taking converter for the bound value's type. The `CreateBinder` and `BindConverter.FormatValue` overloads that take `string format` exist only for eight types: `DateTime` / `DateTimeOffset` / `DateOnly` / `TimeOnly`, and their `Nullable<>` forms. Writing a format on any other type leaves the call unable to bind inside the generated code, becoming CS1503 — which, per A.0, never reaches the author, so this stops it at the call site. The set of accepted types is drawn from `Microsoft.AspNetCore.Components.EventCallbackFactoryBinderExtensions`'s metadata and is not written out here, matching §4.1's standard (check it when doing so is only copying a table the framework already ships as canonical) and the precedent BCF3028 sets by drawing on `[EventHandler]`. The generator emits two calls, `FormatValue` and `CreateBinder`, and the set of format-taking overload types agrees between them; to keep the fact that only one is read from going unnoticed, `BindFormatTableSyncTests` pins that the two tables agree (the same posture as `KnownSymbolsSyncTests` pinning the curated table and the void table against each other in both directions). A compilation that cannot resolve the table skips the check entirely and silently, but because the assembly that declares `ElementView` references `Microsoft.AspNetCore.Components`, any compilation that can spell `.Bind` can always see this type — a defense, not an expected path. Culture is not a factor: any type this surface can bind can be written. The only restriction is on format. The position is the format argument, since that is what the author must delete or rewrite. The message does not assume which type reached the rule and instead names the type it found (the same shape as BCF3023) |
| BCF3032 | Error   | A keyed `ForEach`'s content root also writes its own `.Key`. `SetKey` ends up called twice on the same frame, and whichever is emitted later overwrites the key field, silently killing the earlier one — which one survives follows from emission order, not from any priority the author wrote, so this is rejected. The judgment is answered at the same time by the walk that resolves the root's kind (`KeyabilityResolver.ResolveRootKind`), and, like BCF3003, is reported once per definition regardless of reachability. A `ForEach` with `key: null`, and its spread sugar, are excluded, since they emit no `SetKey` — there, the root's `.Key` stands as the only key. It never fires together with the same shape as BCF3003: a shape whose root becomes a region has no receiver to write `.Key` on, and that is BCF3008's concern |
| BCF3033 | Error   | The same non-attribute frame decoration (`.Key` / `.Ref` / `.RenderMode`) is written twice on the same element or component. All three fail to match what was written, and each breaks differently by channel (§2.7(E)). This has a separate ID from BCF3010, which looks at duplicates in the attribute and event channels: 3010 is a rule about attribute names and event names, and adding these nameless channels to the same row would make one row state two rules. The position is the second decoration's name, since that is what needs deleting. The message takes the decoration name as an argument (the same shape as BCF3026) |
| BCF3034 | Error   | `.RenderMode` is written on a type that declares an attribute deriving from `Microsoft.AspNetCore.Components.RenderModeAttribute` — an attribute the author declared themselves. The framework's `RenderModeAttribute` is abstract and ships no concrete derivation of its own (Razor generates a derived class per `@rendermode` directive). This also applies when a base class has it; the judgment walks the chain of base types, because the framework reads it the same way, and stopping at the derived type would let this diagnostic's replacement — the runtime throw — pass straight through: at run time, `ComponentFactory` throws `InvalidOperationException` (`The component type '…' has a fixed rendermode of '…', so it is not valid to specify any rendermode when using this component.`). The judgment is a unary predicate over the component type, and since the attribute is also present in metadata, it is decided the same way for a type in a referenced assembly. Because the declared shape is fixed, no call-site specification can ever pass, so which types can and cannot take one is settled per type. This meets `DESIGN.md` §4.1's standard (check it when doing so does not commit this repository to authoring and maintaining a table the check depends on) — what is consulted here is only the presence of one attribute, and there is no table to maintain |
| BCF3035 | Error   | An event modifier (`.PreventDefault` / `.StopPropagation`) is written before any decoration that writes an event. The decorations that write an event are the three of `.On`, an event shortcut, and `.Bind`, and which one is immediately preceding is decided by the chain (§2.7(A)). A modifier carries no event name of its own, so there is no reading of it other than attaching to the immediately preceding event. Emitted with nothing to attach to, it outputs an attribute name no handler on that element responds to, and the framework passes it through silently (measured). The position is the decoration name, since that is what the author must move or delete. There is no guarantee that no event exists on the element — if it is written later in the chain, the fix is to move the modifier there — so the position cannot name the event. The message takes the decoration name as an argument |
| BCF3036 | Error   | The same event modifier is written twice on the same event. Which event is the target is decided by the same chain-walking-backward as BCF3035, so a bound event is equally a target. This breaks the same way BCF3033 reports for non-attribute frame decorations, but carries a separate ID: 3033's item targets the three that are "not" attribute frames, while these two are attribute frames (§2.7, measured). The two modifiers are separate channels, so `.PreventDefault().StopPropagation()` is not a duplicate. The position is the second decoration, since that is what needs deleting (the same as BCF3033). The message takes the decoration name and the event name as arguments |
| BCF3038 | Error   | An event modifier is disabled by that event's `[EventHandler]` registration. Blazor carries per-modifier permission in `EventHandlerAttribute`, and even when an attribute for a disabled combination is emitted, the rendering side silently ignores it. Razor reads the same registration and rejects the same combinations, so lacking this check means falling short of Razor on this one point (`DESIGN.md` §4.1). The number picks up after BCF3037; 3037 was retired in #370, and numbers are not reused (`CONTRIBUTING.md` §Conventions the code must uphold, 付録B.18). The four-argument constructor's parameter order is `(attributeName, eventArgsType, enableStopPropagation, enablePreventDefault)`, the reverse of what intuition suggests (measured, 2026-08-15, `Microsoft.AspNetCore.Components.Web` 10.0.10). There are 96 registrations; `preventDefault` is enabled in all 96, and `stopPropagation` is disabled in only two, `oncancel` and `onclose`. The two-argument constructor sets both to `false`, so a custom event the author registers themselves rejects both by default — this is why this diagnostic is not confined to a rule about two events. The source and gatekeeper are the same as BCF3028's; an event with no registration is not reported. Where the same name carries two conflicting registrations, including a case where only the flags differ with the same argument type, it is dropped from the table (under-reporting is the safe side). The judgment sits at the event modifier's target-resolution point, and both an `.On`-derived and a `.Bind`-derived event pass through this same single place (§2.7(A)). The position is the modifier's decoration name, since that is what the author must delete (the same as BCF3035). The message takes the decoration name and the event name as arguments |

## 付録B: 検討した代替アーキテクチャと不採用理由

**B.1 Interceptor方式(C# 14)**: `Body` を実行時に評価し、各設計時API呼び出しサイトをInterceptorで静的シーケンス付き実装へ置換する方式。呼び出しサイト置換自体は成立しますが、次の3点により採用しませんでした。(a) 実行時評価を前提とするため、装飾チェーンの合成型に対する統一戻り値型が構成できません(C#に不透明戻り値型が存在せず、`ref struct` はインターフェースへ変換できない)。(b) `[InterceptsLocation]` の位置指定子がソース変更のたびに再計算され、ビルドパイプラインが位置データに敏感になります。(c) 本方式(全体生成)が採用可能である以上、部分置換に固有の利点がありません。

**B.2 ランタイム `ref struct` ツリー方式**: 要素を `readonly ref struct` としてスタック上に構築し、実行時に `Render` を再帰呼び出しする方式。GC回避には有効ですが、次の3点により採用しませんでした。(a) 可変個の子要素を受け取る手段がありません(`ref struct` は配列・`params` に格納不可、ジェネリックオーバーロードはアリティ上限を持つ)。(b) B.1と同じ戻り値型問題があります。(c) 静的サブツリーのキャッシュと両立しません(`ref struct` はフィールド格納不可)。本方式(生成コードによる直接発行)は、同じゼロアロケーション特性を型システム上、無理なく達成します。

**B.3 `ChromeLayoutBase` を `BodyComponentBase` から派生させ `SetParametersAsync` で介入する方式**: レイアウトを通常のBlazorCodeFirstコンポーネントと同じ基底型に載せる方式。Blazorが渡す `Body` パラメータを `SetParametersAsync` で抜き取ってから、残りのパラメータを基底へ転送します。当初はこの案を採る判断をしていましたが、実装して実行した結果、成立しないことが確認されたため撤回しました。残りのパラメータを転送する唯一の公開手段は `ParameterView.FromDictionary` です。ところがその列挙子は `cascading: false` を固定値で返します。そのため、cascading値のみを受け取るプロパティに対して `ComponentProperties.SetProperties` が例外を投げます(*"The property 'X' … cannot be set explicitly because it only accepts cascading values."*)。影響は `[CascadingParameter]` に限りません。この検査は `CascadingParameterAttributeBase` を基準とするため、`[SupplyParameterFromQuery]` も同じ理由で落ちます。認証テンプレートが標準で用いる `[CascadingParameter] Task<AuthenticationState>` も、レイアウトで受け取れなくなります。加えてナビゲーションごとに `RenderTreeFrame[]` を確保します。採用した方式(`ChromeLayoutBase : LayoutComponentBase`)は、Blazorが名前で要求する `Body` を正しい名前のまま継承します。`SetParametersAsync` に付与された `[DynamicDependency]` トリマーヒントもそのまま引き継ぐため、プラットフォームのパラメータ結線と競合しません。教訓として、プラットフォーム側のパラメータ結線に介入する方式は本設計では採りません。

**B.4 `[ViewPart]` メソッドに `〜AsFragment` 兄弟メソッドを併生成する方式**: 各 `[ViewPart]` に対し `RenderFragment` を返す静的メソッドを生成する方式。既存の `.razor` から `@Widgets.StatusBadgeAsFragment(status)` の形で、コードファーストUIの断片を埋め込めるようにします。`DESIGN.md` §6.1 と `CONTRIBUTING.md` の不変条件が当初これを約束していましたが、実装されたことは一度もなく、#144 で撤回しました。理由は4点です。(a) この方式が満たそうとした要求は、コンポーネント粒度ですでに満たされています。`.razor` からBlazorCodeFirstコンポーネントをタグとして名指すことに同一プロジェクト制限はなく、`site/BlazorCodeFirst.Site/App.razor` が現にそうしています。Razorが解決するのは作者が書いたクラス名であり、生成物は `RenderView` の本体だけだからです。(b) 生成される兄弟メソッドは実体を持つため参照元アセンブリから呼べてしまい、「静的展開は宣言のソース構文を要するため同一コンパイル内に限られる」という `[ViewPart]` の境界(§4.3、BCF1002)に例外を作ります。同一の属性が「呼び出しサイトへ展開される同一コンパイル内の仕組み」と「公開APIを生やす宣言」という二つの顔を持ってしまい、`[ViewPart]` と `Component<T>()` の使い分けを説明できなくなります。(c) 実装は次の3つを新たに必要とします。含有型への `partial` 要求(現行の `[ViewPart]` にはなく、`site/BlazorCodeFirst.Site/Pages/NotFoundContent.cs` は非partialの `static class` です)、`〜AsFragment` の名前衝突に対する診断、`private` な `[ViewPart]` に対する無用な兄弟の扱いです。さらに、同一プロジェクトの `.razor` が生成された静的メソッドを呼べるかは未検証です。これはBCF3012を生んだのと同じ「ソースジェネレータは互いの出力が見えない」領域にあり、不成立なら本方式は参照先アセンブリからしか使えず、その場合は(a)のコンポーネント経路が常に優ります。(d) 得られるのはコンポーネントより細かい断片粒度の埋め込みのみで、代替手段は `BodyComponentBase` で包むクラス1つです。教訓として、再利用の単位も相互運用の単位もコンポーネントとし、`[ViewPart]` は同一コンパイル内の分割手段に徹します。

**B.5 同一要素の2つ目の `.Bind` をBCF3021で拒否する方式**: 1つの要素に双方向束縛が2つ以上現れたら、2つの名前がいずれも空いていてもコンパイルエラーとする方式。#71で実装して出荷しましたが、#162で撤回しました。根拠としていたのは「`SetUpdatesAttributeName` の記録先は要素であり、2つ目の束縛が1つ目の再同期先を上書きする」という主張です。この主張は#71自身の最終レビューで誤りと指摘されましたが、指摘は解消されないまま規則だけが出荷されました。#162で実測した結果は次のとおりです。`SetUpdatesAttributeName` が名前を書くのは要素ではなく直前の属性フレームです。生成コードは束縛ごとに属性フレーム・イベントフレーム・`SetUpdatesAttributeName` の順で出します。したがってここでいう直前の属性フレームはその束縛自身のイベントフレームであり、読み戻す `RenderTreeUpdater.UpdateToMatchClientState` が見るのもイベント自身のフレームです。つまり書き込み先と読み出し元は同一のフレームであり、そのフレームが束縛ごとに別であるため、同一要素の2つの束縛は互いの再同期を壊しません(§2.7(A))。残る選択は、別の根拠を立て直して規則を維持するか、規則を落とすかでした。落としたのは `DESIGN.md` §4.1 の原則によります。この表層が検査するのは妥当性ではなく翻訳の破れであり、2つの束縛の背後に破れはありません。Blazorはこの形を通常の差分検知で正しく描き、動機となる形も実在します(双方向のプロパティを2つ以上持つWeb Component、`DESIGN.md` §4.1)。同じ原則が付録Dの計測済みの残余を未検査のまま置いている以上、何も破らない形だけを拒否する位置は取れません。撤回は欠番の解放ではありません。プレビュービルドでこのエラーに当たった読者が番号で検索したとき、別の規則が同じ名前を着ていてはならないためです。`AnalyzerReleases.Shipped.md` が空である以上、`CONTRIBUTING.md` のID再利用禁止はこの番号に届きません。そこで `DiagnosticExpectations.RetiredIds` と `DiagnosticTableTests.RetiredIds_AreNeitherDeclaredNorDocumented` が、BCF3021が記述子にも付録Aにも戻らないことを機械的に固定します。教訓として、プラットフォームの挙動についての主張を根拠に置く診断は、その挙動を実測してから出荷します。根拠への指摘を解消しないまま出せば、指摘のほうは記録に残らず規則だけが残ります。

**B.6 void性を `ElementView` の型で表現する方式**: void要素13タグのcuratedヘルパーが、インデクサを持たない `VoidElementView` を返す方式。`Img["child"]` はBCF3016ではなくCS0021になり、表層はHTMLに居場所のない形を差し出さなくなります。§4.1の系譜のうち3つがこの経路を採っています。Giraffe.ViewEngineの `XmlNode` は `VoidElement` ケースを持ち、`br []` がリストを1つ取るのに対し `div [] []` は2つ取ります。Falco.Markupは `ParentNode` と `SelfClosingNode` に分け、`_hr [ _class_ "divider" ]` と書きます。TyXMLは多相バリアントの内容モデルに符号化しています。#179で検討し、採用しませんでした。理由は4点です。(a) 得られるのは形だけです。どちらも今日すでにコンパイルエラーであり、BCF3016はこの誤りのために書かれた文面を持つのに対し、CS0021は「インデクサを適用できない」としか言いません。表層は読みやすくなり、診断は読みにくくなります。(b) コストは `Decorations` に落ちます。装飾は22個すべてが `ElementView` を受けて `ElementView` を返す形です(`Decorations.cs`)。チェーンを通してvoid性を保つには、void型のために全体を複製するか、自己参照制約を持つビルダーインターフェースで全体をジェネリックにするかのいずれかを要します。どちらも大きく、しかも新しい装飾が必ず触るファイルに払われるため、#156と#178がそれぞれ高くつきます。(c) 検査は消えません。`Element("br")["x"]` は文字列経路で同じタグに達し、そこには変えるべき型が存在しないため、BCF3016はいずれにせよ必要です。型が覆うのはこの検査のcurated側の半分だけになります。§4.1は両経路が単一のタグ文字列に落ちてから同じ表を引くことで構成上一致すると述べており、片方の経路しか覆わない型規則はその逆の配置です。(d) ミラーとしての論拠は見かけより弱いものです。`DESIGN.md` §4.1が引く境界はタグ単独から決定できるかであり、void性はその内側にあります。設計はこれを型の領分から外し、検査の領分として扱っています。本項は付録Dと同じ意味での記録であり、再検討には上の4点が答えていない理由を要します。

**B.7 クラスチャネルの区切りを条項側へ寄せる生成規則**: 各項を `((t) is { } __c ? " " + __c : "")` の形で出し、区切りを項自身に持たせる方式。#177 の設計で採用しましたが、外部レビューの指摘により実装前に撤回しました。アロケーションが増えるためです。非nullな項では余分な空白がそもそも出ないため、`.Class("card").Class(_variant)` のような最頻形で、何も得ずに1回から2回になります。`class` はこの表層で最も多く書かれる属性であり(#177)、その値が非定数であることは普通です。同じレビューは代案も出しました。定数プレフィックスを条件の両腕へ畳む `((t) is { } c ? "card " + c : "card")` です。これは1アロケーションで空白も出ませんが、1つ目の項が条件付きのとき畳めるプレフィックスが存在しないため、`.Class(a ? "card" : null).Class(b ? "active" : null)` の残余には届きません。採用したのは、生成クラスが自身のために持つ `private static` の join が実行時に `null` の項を飛ばす方式です(#236)。両方の残余に届き、非nullな項しか無い綴りでは連結演算子と同じ `string.Concat` 1回に落ちます。3形の実測で、変更前後のアロケーションが一致することを確認しています(`ClassChannelBenchmarks`)。教訓として、値の有無が実行時にしか決まらない規則は、実行時に判定する場所を1つ作るほうが、生成する綴りの側で場合分けするより安く済むことがあります。

その join を生成クラス側に置くか、`BlazorCodeFirst.Runtime` の1メソッド `JoinClasses(params ReadOnlySpan<string?>)` にするかは、#239 で問い直し、2026-08-14に実測しました(`ClassChannelBenchmarks` の join site 行)。周りのフレーム呼び出しは両案で同一であるため、join の式だけを計測しています。1メソッド側の本体は、規則を保つ最も速い綴り、すなわち `null` の項を落としてから残りを繋ぐ形にしました。区切りを織り込んでから連結する綴りは `n` 項に対して `2n-1` スロットを書いて読み直すためどの arity でも遅く、その綴りで測れば生成クラス側の勝ちを計測ではなく記録することになります。#239 のアロケーションについての予測は当たっています。`params ReadOnlySpan` の実引数バッファを呼び出し側がスタックに取るため、5形すべてで両案は同値でした(2項40 B、2項で片方が `null` は0 B、3項48 B、4項64 B、4項で2つが `null` は40 B)。時間は一方的ではなく、速い側が形で入れ替わります。生成クラス側が勝つのは、項が2つの形(7.17 ns 対 12.03 ns)と、項に `null` がある形です。後者の差は大きく、2項で片方が `null` なら 0.15 ns 対 2.46 ns になります。arity 2 のラダーは `null` 判定2つと `string.Concat` 1回であるためインライン展開され、片方が `null` なら残った項を返すだけになるからです。1メソッド側は、項を走査して詰め直す本体をどの形でも通ります。負けるのは `null` を含まない3項(22.58 ns 対 19.33 ns)と4項(26.80 ns 対 21.96 ns)で、差は14〜18%です。生成クラス側を採ったのは、勝つ側の形がこのチャネルで実際に書かれる形だからです。#236 がこの規則を作った動機は条件付きの項、つまり実行時に `null` になる項であり、そこがラダーの最も得意な形です。`site/BlazorCodeFirst.Site` は `.Class` を160箇所で書きますが同じ要素に2つ載せた箇所は無く、生成された12クラスのどれも join を持ちません。代わりに払い続けるのは #239 が数えた3つ、すなわち arity ラダー、`IndentedWriter.WidestClassJoin` を通る arity の受け渡し、ジェネリックコンポーネントでの型引数ごとの実体化です。公開表層の費用は #239 の見積もりより小さいものでした。生成コードが呼ぶ先の `BlazorCodeFirst.CompilerServices.ViewRuntime` は既に `[EditorBrowsable(Never)]` であるため、利用側のIntelliSenseに現れる費用は存在しません。この判断は arity の分布に賭けています。再検討には、`null` を含まない3項以上が普通に書かれることの測定が要ります。

**B.8 HTMLコアの上にオピニオンなレイアウト/コンポーネント層を重ねる方式**: `VStack` / `HStack` / `Card` / `Modal` / `PrimaryButton` といった語彙を、HTMLプリミティブへ展開するopt-inの第二パッケージとして与える方式。実装は事前にクラスを付けた要素を返す関数(`static ElementView VStack(int gap = 4) => Div.Class($"flex flex-col gap-{gap}");`)です。#74で検討し、採用しませんでした。理由は5点です。(a) 別アセンブリであることは `DESIGN.md` §4.1 の却下に答えていません。あの却下が退けているのは「ライブラリがレイアウトに二つの答えを持つこと」であって、二つ目がどのアセンブリで出荷されるかではありません。ファーストパーティのパッケージは存在した瞬間に公式の答えになります。ドキュメントがそれを実演し、新規の読者はHTML表層より先にそれを学びます。しかも `Card` はDOMに `Card` として現れないため、§8がDOMネイティブであることに帰している資産(アクセシビリティツリー、CSSエコシステム、DevToolsで読めること)の側から見た1:1の対応が崩れます。(b) この層はフレームワークに何も要求しません。上の `VStack` は既存表層の上の1行の静的メソッドであり、コンパイラ側にもランタイム側にも新しい機構を要しません。作者が自分のプロジェクトに数行で書けるものをファーストパーティで出荷することは、`PublicAPI.*.txt`・バージョニング・ドキュメント・後方互換義務を持つ第二の公開表層を、何も買わずに抱えることです。(c) CSSフレームワークを選ばされます。上のクラス文字列はTailwindのユーティリティです。選択肢は3つあり、いずれも取れません。Tailwindに依存する(§8がCSSエコシステムをそのまま使えると述べている主張を、作者の代わりに一つ選ぶことで裏切ります)、自前のCSSを積む(MudBlazor / Radzen / Fluent UIと正面から競合するコンポーネントライブラリという別プロダクトになり、そこに本ライブラリの優位はありません)、CSSなしでクラス名だけ出す(役に立ちません)です。(d) 例示のコードはそもそも動きません。`$"gap-{gap}"` は補間であるためTailwindのコンテンツスキャナから見えず、purgeされます。(e) 名前で逃げられません。`BlazorCodeFirst.UI` は「BlazorCodeFirstのUI部分」と読め、コアがUI層ではないことを含意する点で逆立ちしており、より重要なことに、`BlazorCodeFirst.*` という名前そのものが公式の答えとして読ませます。示唆的な先例はElmで、`elm/html` がコアであるのに対し、HTML/CSSの考え方ごと置き換えるレイアウト語彙 `mdgriffith/elm-ui` はサードパーティであり、別の名前を持ち、`elm/html` の上に重なるのではなく併存しています。この方式をいつか作るなら、その形(独自の名前・独自のリポジトリ・BlazorCodeFirstの *上に* 建てる)が誠実な位置取りであり、`DESIGN.md` §9が周辺構想を「本筋の設計とは独立した別プロダクトの検討事項」としている記述とも一致します。#74は当初、この判断を「再利用可能なラッパーにコンテンツを渡せないこと」の解消待ちとしていました。語彙を剥ぎ取った後に残る本当の需要は、ラッパー要素も実行時コストも持たない再利用可能でパラメータ化されたUIの断片であり、その機構は `[ViewPart]` としてすでに存在していたものの、当時は `View` 型のパラメータがBCF1002で拒否されていたためです。これは#176で着地しました(`SlotView` と `Slot`、`DESIGN.md` §4.3)。前提が揃った以上、この判断は保留ではなく不採用です。再検討には、`[ViewPart]` のコンテンツ経路では担えない具体的な事例が要ります。教訓として、第二の語彙が必要に見えるときは、まず一つ目の語彙に欠けている合成手段を疑います。

**B.9 `View` にインデクサを付け、部品の戻り値型を1つに統一する方式**: `View` 自身が `params ReadOnlySpan<View>` のインデクサを持ち、`[ViewPart]` の戻り値型を `View` だけにする方式。コンテンツを取る部品も取らない部品も `View` を返し、`Card("x")[P["本文"]]` は `View` のインデクサで通ります。動機は、作者が部品を宣言するたびに `View` と `SlotView` を選ぶ負担を消すことであり、§4.1の系譜が例外なく戻り値型を1種類しか持たないという観察がその後押しになります。2026-08-11に検討し、採用しませんでした。理由は5点です。(a) 他の系譜が1型で済む理由は戻り値型の側にありません。**組み込み要素の子チャネルがふつうの関数パラメータである**からです。Giraffe.ViewEngine / Falco.Markup / Feliz.ViewEngine は `XmlNode list` 相当の位置引数です。kotlinx.htmlは末尾ラムダ(`fun FlowContent.card(title: String, block: DIV.() -> Unit)`、戻り値は子を取る部品も取らない部品も `Unit`)。Plotは `@ComponentBuilder` クロージャ(`struct NewsArticle: Component { var body: Component }`)。いずれも自作部品は同じ型のパラメータを1つ宣言すれば組み込みと同じ形になり、再現すべき機構が存在しません。この表層の子チャネルはインデクサであり、C#ではメソッドが自分の呼び出し式にインデクサを生やせません。インデクサを持てるのは型だけであるため、部品に組み込み要素と同じ形を与えるには型が要ります。§4.3が角括弧を選んだ時点で2つ目の戻り値型はその帰結であり、独立に選び直せる項目ではありません。(b) 同じ問題を持つ唯一の先例が、同じ区別を引いています。§4.1の系譜のうち子の構文が独自の構成物であるのはOxpecker.ViewEngineだけで、そのCEメンバーは具体型ではなく `HtmlContainer` インターフェースの型拡張として定義されます。すなわち「子を取れるもの / 取れないもの」を型で区別しており、C#に写せば `ElementView` と `SlotView`(取れる)対 `View`(取れない)という現行の配置そのものになります。(c) 型が無償で閉じている規則が診断へ移ります。§4.3が挙げる3つは、角括弧の書き忘れ `Div[Card("x")]`、#176が退けた位置引数の綴り `Card("t", P["本文"])`、そして装飾です。これに加えて `Div["a"]["b"]`・`Fragment(…)["x"]`・`Raw(…)["x"]`・`If(…)["x"]` が新たにコンパイルを通ります。装飾だけは変わりません(装飾は `ElementView` の拡張であり、`View` にインデクサを足してもレシーバの型は動かないためです)。新設が必要な診断はBCF3025の1つから4〜6個になります。付録B.6が退けたのは型を増やして検査を減らす向きであり、本項はその逆向きに同じ交換レートで払う案です。(d) そのうち少なくとも1つは構文検査に届きません。`var v = Card("x"); Div[v];` は、直書きの `Div[Card("x")]` と違って、`v` がスロットを持つ部品の戻り値であることを構文から判定できません。この設計の解析は構文主導であり、SSCの外は諦めてBCF2001を出すと決めています(§5.3)。型は変数に付いて回りますが構文の検査は付いて回らないため、この経路は診断を書いても黙って空のコンテンツを描画します。付録Dが記録している未検査の残余を、いま型で閉じている領域から新たに作ることになります。(e) 交換の両側を同じ人が払います。減るのは部品の宣言ごとに戻り値型を1回選ぶことで、誤れば宣言位置でBCF1002(`View` 戻り値に `View` パラメータ)かBCF3025(`SlotView` 戻り値に `Slot` が無い)が即座に報告します。増えるのは呼び出しを書く側で黙って壊れた描画を受け取る余地です。どちらも作者が払うため、これはライブラリと作者の間の取引ではなく、必ず気づく費用と気づかない費用の交換になります。再検討には、(c)と(d)が挙げた書き方が実際には生じないことの測定を要します。C#の流儀として戻り値型の2択が不自然に見えるという観察はその測定に当たりません(§4.1)。

**B.10 `.Param` のセレクタをより短い綴りへ置き換える方式**: `Component<T>().Param(b => b.Label, label)` が、名前を与えるためだけに毎回ラムダを1つ書かせている点を縮める方式。#170で検討し、2026-08-11に採用しませんでした。まず、どの候補も超えられない壁が1つあります。呼び出しサイトで名指せるのは作者が書いた宣言だけです。ソースジェネレータは自分の出力を観測しないため、コンポーネントごとに生成したメンバー(`StatusBadge.Of(label: "x")`、生成した `.Label(…)` 拡張メソッド、生成したビルダー型)は、生成器が走っている間その呼び出しサイトで束縛しません。しかも失敗の質が悪く、参照先プロジェクトの生成メンバーはメタデータ経由で解決するため、別アセンブリのコンポーネントでは通り同一プロジェクトでは通らないという、BCF3012の非対称がBCF間の合成が主に使う向きで再生します。`Component<T>()` そのものも開いていません。`Div` が括弧を要さないのはプロパティだからであり、C#にジェネリックプロパティは無いため `Component<T>` はプロパティになれません。動かせるのは `.Param` の側だけであり、既存のC#構文に限れば候補は3つです。(a) 文字列名 `.Param("Label", label)`。要素側の `.Attr(name, value)` との一貫性が根拠に見えますが、論拠は逆向きです。`DESIGN.md` §4.1が属性側を文字列にしたのは、属性の語彙がそもそも開いていて閉じた集合として写せる対象が存在しないからでした。コンポーネントのパラメータ集合は宣言済みで閉じており、型も付いています。値の型照合はいまC#が無償で行っており(実測。`.Param(b => b.Compact, 42)` はCS0029/CS1662です)、文字列名にすればこれを新しい診断として書き直すことになります。`DESIGN.md` §4.3がマーカー型の名前付きチャネルを退けた理由 ── 位置引数ならC#のオーバーロード解決が無償で与えるものを作り直す ── と同じ形です。加えてIDEのリネームが追従しなくなり、これは `DESIGN.md` §1.4 がこの設計の実在価値として挙げる2つのうちの一方です。(b) オブジェクト初期化子 `Component(new StatusBadge { Label = label })`。型検査もリネームも保ちますが、設計時式の中に本物のコンポーネント実体を作ります。`DESIGN.md` §5.1は設計時APIの実体がすべて慣性であることを、同 §1.3は実行時のヒープ割り当てが原理的に発生しないことを述べており、どちらも破れます。#68(設計時APIを設計時式の外に書いても何も起きない件)も悪化します。いま何もしない式が、本当に割り当てるようになるためです。(c) 初期化ラムダ `Component<StatusBadge>(b => { b.Label = label; b.Compact = compact; })`。3つのうち唯一、型検査・リネーム・慣性のすべてを保ちます。それでも採らない理由は2点です。第一に `.Param` が消えません。`.Bind` はセレクタから `{名前}Changed` と `{名前}Expression` を導くためにセレクタを要し、`.Template` の文脈を読む形は引数自体がラムダであるため、どちらも代入の形を持てません。したがって代入形は `.Param` を置き換えるのではなくその隣に並び、同じことの二通りの綴りを持たないという `DESIGN.md` §4.1・§4.3 の位置に反します。第二に、`.Param` が束縛を呼び出し1つずつに分けることで構造的に閉じている検査が、診断へ移ります。ブロック本体には、単純代入でない文も `[Parameter]` でないプロパティへの代入も書けます。そのためBCF3005(セレクタが単純なプロパティ選択でない)とBCF3006(対象が settable な `[Parameter]` でない)がいま構文の形で見ているものを、文の集合に対して見直すことになります。そして、この方式が縮めようとしている繰り返しには別の答えがすでにあります。`[ViewPart]` で1回包めば呼び出しサイトから `Component` も `.Param` も消え、名前付き引数と省略可能引数がそのまま使えます(`DESIGN.md` §4.3)。属性は宣言ごとに1つであり、呼び出しサイトごとではありません。再検討には、`[ViewPart]` の包みでは担えない事例が要ります。C#の流儀としてラムダの繰り返しが冗長に見えるという観察はその事例ではありません(`DESIGN.md` §4.1)。

**B.11 `[ViewPart]` を属性なしで自動適用する方式**: `View` を返す静的メソッドを、属性の有無にかかわらず静的展開の対象とする方式。動機は、属性を書く手間と、付け忘れて黙って動的経路(§2.3 Opaque)に落ちる事故を同時に消すことです。2026-08-11に検討し、採用しませんでした。理由は4点です。(a) 展開できる集合は「`View` を返す静的メソッド」より狭いものです。BCF1002が列挙するとおり、1つの `return` へ到達する本体であること・ジェネリックでないこと・`params` や参照渡しや `ElementView` のパラメータを持たないこと・本体が静的にシーケンス可能であること等を要します。自動化とは、この条件を満たさない残りをどう扱うかを決めることであり、分岐は2つしかありません。(b) 満たさない宣言をエラーにすると、`DESIGN.md` §5.3が意図して残している逃げ道の綴りが消えます。解析できないコードを書きたいときは属性を付けずに書けば動的コンテンツとして通る、という経路が無くなり、代わりにopt-outの属性が要ります。注釈すべき側が入れ替わるだけで、属性は減りません。(c) 満たさない宣言を黙って動的経路へ落とすと、必ず気づく費用が気づかない費用に変わります。§2.7のとおり展開はフレーム列が呼び出しサイトへ直書きしたものと一致し、動的経路はリージョンで包んで実行時に `RenderFragment` を描くため、割り当ても静的最適化も違います。いまは作者が属性を書いた時点で「展開されるつもりだ」と表明しており、成立しなければBCF1002が宣言の位置で即座に報告します。自動化すると、本体にネイティブの `foreach` を1つ足しただけで黙って性能が落ちます。付録B.9(e)が退けたのと同じ交換です。(d) 対象メンバーの範囲を別に決めることになります。`Body` と `Chrome` は `View` を返すプロパティであり、インスタンスメソッド・ローカル関数・ラムダも候補になります。属性はこの集合を宣言の側で閉じており、自動化はその規則を作者が覚える側へ移します(`DESIGN.md` §4.1)。なお動機に挙げた付け忘れは実在の事故であり、それには属性を自動にするのではなく呼び出しサイトの診断で答えます。当初はBCF2001(Info)を予定していましたが、実装時に前提が誤っていることが分かりました。`View` に実体を与える綴りは `implicit operator View(RenderFragment?)` だけであり、設計時表層のメンバーはすべて既定値を返します。したがって表層から組まれた `View` はフラグメントを持たず、Opaque経路へ載せても何も描画しません。失われるのは最適化ではなく出力そのものであって、Infoが述べる事実と合いません。答えるのはBCF3030(Error)で、呼び出し先のソース宣言が読める場合にこれが止めます。読めない場合だけがBCF2001の対象として残ります(#260)。この配分でも本項(c)の交換は成立しません。作者は属性を書き忘れた時点で診断を受け取り、黙って落ちる経路はソース宣言の読めない呼び出しに限られます。再検討には、(c)の暗黙の劣化を作らずに済む3つ目の分岐が要ります。

**B.12 コレクションから子を作るための2つの代替**: #172が検討し、いずれも採用しませんでした。同Issueは子リストへコレクションを差し込む綴りを与える作業であり、着地したのは `key: null` と、その糖衣であるスプレッド `Ul[[.. proj]]` の2つです(§2.3 SSC-3)。

1つ目は、`IEnumerable<View>` を取るインデクサのオーバーロードを足して `Ul[proj]` と書けるようにする方式です。理由は3点です。(a) スプレッド形が同じことを書けるうえ、兄弟の子と混ざります。インデクサは1つの引数が子リスト全体であるため混ざりません。(b) `Ul[proj]` は、子を1つ置いたのか子の並びを置いたのかを呼び出しサイトで述べません。`..` は述べます。これは同じIssueがキーの不在を既定値ではなく書かれた `null` にしたときの基準と同じであり、`DESIGN.md` §4.2 が求めているものです。(c) 同じことに2つ目の綴りを足すのは `DESIGN.md` §4.1 が退ける交換であり、しかも子チャネルは4面(`ElementView` / `ComponentView<T>` / `SlotView` / `Fragment`)あるため、1つではなく4つ増えます。実装はランタイムAPIを一切増やしませんでした。再検討には、スプレッド形が実際には書かれない、または読めないことの測定が要ります。

2つ目は、`Select` 以外の `IEnumerable<View>` スプレッドをOpaque(BCF2001)へ縮退させる方式で、#172の起票時点ではこちらを予定していました。理由は2点です。(a) `Div[[.. _views]]` は保存された `View` の読み出しであり、付録AのBCF1003行が「残る形」として名指しているものそのものです。単数形の `Div[_view]` はBCF1003であるため、複数形だけをOpaqueへ通すと、単数より複数のほうが緩いという逆転が起きます。(b) フィールドは呼び出しではないためBCF3030が届きません。表層から組まれた `View` はフラグメントを持たないので、Opaque経路は黙って何も描画しません。付録B.9(e)とB.11(c)が退けているのと同じ、気づかない費用です。したがって `Select` 以外のスプレッドはBCF1003のまま置きました。再検討には、この形がフラグメントを持つ `View` でのみ書かれることの測定が要ります。

**B.13 属性名をメタデータへ載せて装飾を外部から宣言させる方式**: 属性名を属性引数へ載せた静的拡張メソッドを、装飾として認める方式。宣言は `[Decoration("hx-get")] public static ElementView HxGet(this ElementView e, string? value) => e;` の形をとり、呼び出しサイトは `.Attr("hx-get", value)` とまったく同じ形へ降ります。属性引数はメタデータに残るため、`[ViewPart]` と違ってアセンブリを越えます。公開されたHTMXパックが実際に動くということです。降ろした後が `.Attr` と同一であるため、BCF3010・BCF3023・BCF3024・§2.7 (D) はいずれも改造なしで届きます。#242で検討し、採用しませんでした。まず、`DESIGN.md` §4.1 が型付き装飾(`.Padding()`)を退ける理由は、ここには届きません。属性名をそのまま写す限り、覚え直しを強いる語彙にはならないためです。退ける理由は次の3点です。(a) 得られるのは反復の削減だけです。`.Attr("hx-get", url)` が同じ属性を同じフレームで出すため、失われる能力がありません。(b) 費用が属性チャネルに落ちます。ここには BCF3010 / BCF3011 / BCF3023 / BCF3024 が既に載っており、これに宣言形の契約と、契約違反を名指す診断が加わります。契約が要求するのは5つです。静的であること、`ElementView` の拡張であること、`ElementView` を返すこと、名前が非空の定数であること、値が `string?` / `bool` / 値なしの3形に収まることです。さらに BCF3029 が見る慣性集合(`Html` と `Decorations` の全メンバー)へ、参照アセンブリの `[Decoration]` 宣言を集める経路も要ります。その経路が無ければ、パックの利用者が `Body` の外で `e.HxGet("/x")` と書いたとき、出力は生まれず、診断も出ません。#242 はこの項目を数えていません。(c) 拡張点になるのが、規則を持たない列挙です。curatedな要素集合は除外6群という理由で定義されており、標準に要素が追加されればそれは自動的に候補になります(`DESIGN.md` §4.1)。属性ショートカットの7つ(`Href` / `Src` / `Alt` / `Id` / `Type` / `Title` / `Role`)にはその規則がありません。なぜ `Href` があって `For` や `Value` が無いのかは、規則では説明できません。属性の語彙が開いているためです。この集合は閉じたものとして固定されています(2026-08-14決定、#321、`DESIGN.md` §4.1)。閉じた集合を外部への拡張点にすれば、規則を持たない境界がそのまま拡張の仕様になります。Oxpecker.ViewEngine がHTMX / Alpine / ARIAを別パッケージで出荷している事実は、この判断には届きません。あちらは実行時に木を組み立てるため、`attr` を呼ぶ型拡張がエンジンに何も教えないまま成立します。開く決定が存在しない以上、開いた先例にもなりません。この事実が示すのは需要です。無料であれば人は属性パックを書く、ということであり、費用のかかる機構を正当化する事例ではありません。本体をインライン展開する変種は、検討するまでもなく落ちます。ILは本体構文を持たないためアセンブリを越えられず、公開パックが成立しないからです(付録B.4、`DESIGN.md` §4.3 と同じ理由)。再検討には、属性セットをパッケージとして配ることがこの表層の目標に入ることを要します。

**B.14 タグ名と属性名を実行時の値として受ける方式**: 3つの形をまとめて扱います。1つ目はタグを実行時に決める `Element(GetTagName())`、2つ目は属性名を実行時に組み立てる `.Attr($"data-{kind}", value)` です。3つ目が属性を辞書で渡すスプレッド `.Attrs(IReadOnlyDictionary<string, object>)`(Razorの `@attributes`)です。#320と#308で検討し、2026-08-14にいずれも採用しませんでした。まず、両Issueが障害と見ていたものは障害ではありません。#308は「属性の個数が実行時の値であるからスプレッドは静的にシーケンスできない」としていました。実際には `RenderTreeBuilder.AddMultipleAttributes(int sequence, …)` が辞書全体に対してシーケンス番号を1つ取り、それを各属性フレームへ渡します。個数がいくつであっても、シーケンス引数を消費する呼び出しは1回であり、後続ノードの番号も動きません(実測、`AttributeSplatMeasurementTests`)。タグも同じで、`OpenElement(seq, expr)` は静的な番号を1つ取るため、#320が先例として挙げた#17のリージョンすら要りません。ただしそのリージョンは属性には届きません。`OpenRegion` はビルダーの直前の非属性フレーム種別を `Region` にし、続く `AddAttribute` は例外になります(実測、同上)。#17が変えたのは要素とコンテンツの経路の費用であって、属性チャネルの費用ではありません。決めるのはシーケンスではなくクラスチャネルであり、退ける理由は5点です。(a) 畳み込みは検査ではなく翻訳です。見えない検査は黙ればよく、BCF3028 は現に対応表を持たないコンパイルで検査ごと飛ばします。畳み込みにその選択肢はありません。生成器はコンパイル時に値の行き先を選ぶほかなく、どちらを選んでも実行時の値によっては誤ります。自分のフレームへ出す側を選ぶと、名前が実行時に `class` であった要素は `class` フレームを2つ持ちます。スプレッドの無い要素では、これはBCF3024 が拒み、どちらが残るかを規定しないと述べている出力そのものです。スプレッドのある要素では答えが決まっており、その答えのほうが悪い形です。`CloseElement` が重複を解決して後のフレームだけを残すため、畳み込み済みの `class` は延長されるのではなく消えます(実測、同上)。`.Class("card").Class(_variant)` は、辞書が `class` を持った瞬間に両方とも出力から落ちます。チャネルの規則は連結であり(#236、§2.7(A))、その隣に置換という第二の規則が並んで、どちらが働くかをソースに書かれていないキーが決めることになります。(b) 実行時に畳み込む側は選べません。畳み込みはコンパイル時のテキスト連結であり、行き先が変わればフレーム数が変わります(チャネルへ入れば増えず、自分のフレームなら1つ増えます)。フレーム幅は装飾の個数だけで決まり値によって動かないという#234の規則に反し、差を吸収できるリージョンは上のとおり属性の位置に開けません。(c) BCF3010 が、要素の出力についての規則ではなく、名前の綴り方についての規則になります。この診断が拒むのは書いたとおりにならない出力です。スプレッドは重複を例外ではなく常態にし(呼び出し側が既定を上書きできることがこの機構の目的です)、しかもその重複をBlazorが黙って解決します。同じ壊れ方が、2つの名前を書けばコンパイルエラーになり、片方が辞書から来れば黙って後勝ちになります。(d) 出力に現れる名前がソースに現れなくなります。#244 が `.Data(name, value)` を退けたのはこの理由です。属性のショートカットはいずれも、出力する属性名をそのまま綴っています(`DESIGN.md` §4.1)。実行時の名前はその破れを完成させます。`$"data-{kind}"` も辞書も、出力に出る属性名をソースのどこにも書きません。その費用の実物が、#244の引いた `site/` の `data-theme-toggle` です。C#とブラウザ側のJSとPlaywrightのセレクタを、検索だけが結んでいます。(e) タグ側はクラスチャネルと重複検査のどちらにも触れませんが、失うものは同じ種類です。BCF3016 は今日、構成上すべての要素経路を覆っています。curatedヘルパーと `Element` の双方が単一のタグ文字列に落ちてから、同じ表を引くためです(`DESIGN.md` §4.1)。実行時のタグは、その表を引けないタグ文字列を作ります。付録Dが記録しているのは計測して選んだ残余ですが、実行時のタグが作るのは表層が新たに開ける穴です。付録B.6 が「型が覆うのはこの検査のcurated側の半分だけになる」ことを理由に型経路を退けた交換の、向きを変えただけの同じ交換になります。加えて §2.7 (D) の静的畳み込みがその要素で止まります。5点に共通するのは費用の質です。今日この形を書いた作者は、書いた位置でBCF3009 かBCF3011 を受け取り、定数の綴りへ書き直します。経路を開けば、受け取るのは黙って `class` を失った出力か、黙って飛ばされたvoid検査です。付録B.9(e)・B.11(c)・B.12 が退けているのと同じ、気づく費用と気づかない費用の交換です。判断の範囲は要素の属性チャネルです。コンポーネント呼び出しへ属性を渡す経路(#314)は別の問いで、そこにクラスチャネルはありません。再検討には、定数のタグと定数の属性名では担えない事例を要します。属性を辞書で受け取るラッパーを書きたいという観察はそれに当たりません。既知の属性集合は `[ViewPart]` の通常のパラメータで受け取れるため、事例になるのは集合が呼び出し側にしか分からない場合に限られます。

**B.15 curatedヘルパーを全小文字で宣言する方式**: `Div` ではなく `div`、`Span` ではなく `span` と綴り、ヘルパー名をタグ名と文字面まで一致させる方式。#174で検討し、採用しませんでした。まず、C#の側に障害はありません。`KnownSymbols.CuratedTags` の100タグをC#の予約語77語と突き合わせた計測(#174、2026-08-16に再確認)では、衝突は0でした。予約語であるHTMLタグは `object` と `base` の2つですが、どちらも別の理由で除外第4群と第1群に入っています(`DESIGN.md` §4.1)。`select` と `var` は文脈キーワードであり、メンバー名として合法です。したがって全小文字のヘルパーは成立します。成立することは望ましいことを意味しません。推奨形は `using static BlazorCodeFirst.Html;` による非修飾の綴りであり(`DESIGN.md` §4.1)、全小文字にすればそのスコープへ `a` / `b` / `i` / `p` / `s` / `u` / `data` / `map` / `time` / `label` / `form` / `input` / `output` / `source` / `select` / `var` が入ります。いずれもC#では通常のローカル変数の形です。系譜のうち ScalaTags と Elm がこの形で成立しているのは、それらの言語では値の既定の慣習が小文字であり、DSLがホストと争っていないからです。C#では争うことになります。得られるのは文字面の一致だけであり、それを得なくても失うものはありません。先頭一文字を大文字にする現行の対応は全単射であり、逆写像は機械的だからです(`DESIGN.md` §4.1、`KnownSymbolsSyncTests`)。この計測の価値は綴りの選択肢を開くことではなく、大文字化をミラーの限界の一覧から外すことにあります。限界として並ぶのはHTMLの側に写す先があって表層にその像が無い形であり、全単射はそれに当たりません。

**B.16 要素側の `.Bind` が属性名とイベント名を要素から導く方式**: `Input.Type("text").Bind(() => _name)` と書かせ、束縛先の属性名(`value` / `checked`)とイベント名(`oninput` / `onchange`)を生成器がタグと `type` から導く方式。Razorの `@bind` がそう振る舞うため、要素側とコンポーネント側で名前の決め方が割れて見える点を消せます。採用しませんでした。理由は、要素側には推測を照合する先が無いことです。コンポーネント側の推測は `TComponent` の型シンボルを引いて確かめられ、確かめた結果はBCF3020として拒否に使えます(`DESIGN.md` §4.1、**検証できる推測だけする**)。要素側で同じことをするにはタグと `type` を読む必要があります。タグは定数ですが(BCF3009)、`type` は装飾の引数であり式であり得ます。Razorが `type="checkbox"` に対して `value` ではなく `checked` を選ぶときに読んでいるのはマークアップのリテラルであり、この表層の `.Type(kind)` はリテラルではありません。定数でない `type` に対して `value` を既定にすれば、チェックボックスが `value` に束縛されたまま動かず、診断も出ない経路ができます。マークアップのリテラルを読むRazorには無く、この面にだけ生じる失敗です。定数の `type` のときだけ導く変種も採れません。同じ誤りを書き方によって拾ったり拾わなかったりする形になり、`DESIGN.md` §4.1 が `.Bind` のカルチャを推測しない理由として挙げているものと同じです。したがって要素側は推測せず、2つの名前を作者から受け取ります。費用は綴りの長さだけで、取り違えの片側は照合先を要さないためBCF3019が拾います。再検討には、`type` が式であり得る経路で誤らずに名前を導ける機構を要します。

**B.17 `[ViewPart]` を拡張メンバーとして宣言できるようにする方式**: 古典的な `this` パラメータ(`static View Label(this string value)`)、または C# 14 の `extension` ブロックの中に `[ViewPart]` を置き、`"x".Label()` と呼べるようにする方式。#203で検討し、2026-08-09に採用しませんでした。現行はどちらの綴りもBCF1002です。理由は2点で、どちらも綴りの好みではありません。(a) この表層で後置の `.Foo(...)` チェーンは要素への装飾に予約されています(`DESIGN.md` §4.1)。`"x".Label()` はそこに装飾でないものを置きます。`[ViewPart]` が与える呼び出しの綴りは `AppHeader("My Application")` という素の呼び出しだけであり(同 §4.3)、fluentはHTMLに対応物を持たない第二の綴りになります。(b) 受け手はこの表層の値になり得ません。`ElementView` のパラメータは拒否されており、`View` のパラメータはコンテンツスロットであって受け手にはなりません(いずれもBCF1002)。したがって拡張ViewPartは常に他人の型の語彙を伸ばす道具にしかならず、再利用の単位も相互運用の単位もコンポーネントとし `[ViewPart]` は同一コンパイル内の分割手段に徹するという位置から外れます。付録B.4 が `〜AsFragment` を撤回した理由(b)と同じ形です。再検討には、素の呼び出しでは担えない事例を要します。C#の流儀としてfluentが自然に見えるという観察はその事例ではありません(`DESIGN.md` §4.1)。

**B.18 イベント修飾子が `.Bind` のイベントに届かないことをBCF3037で拒否する方式**: `.Bind` の後ろに書かれたイベント修飾子を、解決せずコンパイルエラーとする方式。#368で実装し、#370で撤回しました。B.5 と違い、根拠は正しいままです。撤回したのは根拠が崩れたからではなく、拒んでいた対象が拒む必要のないものになったからです。BCF3037 が防いでいたのは、修飾子がイベントチャネルの末尾へ黙って付き、作者が書いた覚えのないイベントを修飾するという壊れ方でした。この壊れ方は実在し、規則が無ければ診断も出ません。#370 が変えたのは、連鎖の遡りが答えるのを「`.Bind` かどうか」から「どちらのチャネルへ書いたか」へ広げた点です。同じ1回の遡りで対象が決まるため、誤った対象へ付く経路が消え、拒否する対象そのものが無くなりました。番号は再利用しません(`CONTRIBUTING.md` §Conventions the code must uphold)。

## 付録C: 開発時フォールバック案(解釈モード)

§2.6のツーリング検証で、特定環境においてSource Generatorの再実行がEnCに反映されないと判明した場合に限り、次のDEBUGビルド限定フォールバックを導入する余地を残します。

DEBUG構成では、設計時API群を慣性実装から実働実装(`View` に `RenderFragment` を構築して内包する)へ条件コンパイルで切り替え、`RenderView` の代わりに `Body` を実行時評価します。全体は単一のリージョン内で動的シーケンスを用いて描画されます。Hot Reloadは `Body` プロパティ本体の差し替え(EnC標準サポート)として自然に機能し、SGの再実行に依存しません。RELEASE構成では本仕様の生成コード経路のみが用いられるため、出荷物の性能・サイズ特性に影響しません。

本案は開発時と実行時で描画経路が二重化する複雑性を伴うため、§2.6のツーリング確認で必要性が示されるまで導入しません。

## 付録D: 検査しない翻訳の破れ(計測済み残余)

`DESIGN.md` §4.1 は、検査するのが妥当性ではなく翻訳の破れであることを述べます。そして境界を、検査が依拠する表をこのリポジトリで著述して維持することになるかどうかに置き、単項側の最初の対象としてvoid要素に子を与える形(BCF3016、付録A A.1)を挙げます。この基準は#155で改訂しましたが、本付録の除外はいずれも改訂後の基準でも除外のままです。どれもここで表を著述することになるためです。本付録は、そこで検査の外に置かれた残余の一覧です。§4.1 が列挙を持たないと述べている先がここであり、curatedタグに対する `KnownSymbols.CuratedTags`、void要素に対する付録A A.1 と同じ位置にあります。本付録が載せるのは破れであって、妥当でない出力ではありません。`Div.Href("/x")` のように、書いたとおりに出て両描画経路が一致する形は、検査しない点では同じでもここには載りません(`DESIGN.md` §4.1、#335)。

**これは作業の一覧ではありません。** 各項は計測の結果として選ばれた位置の記録であり、BCF3016を広げるためのto-doではありません。付録Bと同じく、再検討には新しい証拠と本付録の改訂を要します。「診断があったほうが親切だ」は証拠ではありません。

計測はいずれも2026-08-03、net10.0 / ASP.NET Core 10.0.10 / Chromiumです。

### D.1 単項側: BCF3016が覆わない破れ

要素タグ単独から決定できるにもかかわらず、BCF3016の対象ではない形です。BCF3016が覆うのは「その要素が子を持てるか」という一つの問いであり、以下はそれぞれ別の検査と別の直し方を要するため、どれもBCF3016には畳み込めません。

**要素の子を取る `textarea` / `title`**。これらは内容をテキストとして読みます。要素の子はページがパースされた時点で潰れ、`appendChild` でDOMを組んだ場合は残ります。`Textarea[Span["x"]]` の `value` は、prerenderでは `"<span>x</span>"`、interactiveでは `""` です。`Textarea` はcuratedヘルパーであるため、この形は `Element` を経由せずに書けます。

**生テキスト要素にエスケープ済みのテキストが届く形**。`AddContent` はエスケープします。`script` / `style` / `xmp` / `plaintext` / `noembed` / `noframes` / `noscript` / `iframe` は内容を生テキストとして要求するため、エスケープが破壊になります。`Element("script")["if (a < b) alert(1);"]` は `<` / `>` / `&` / `'` がunicodeエスケープに置き換わった本体を出し、それらは演算子位置では不正であるためJSの構文エラーになります。`Element("style")` はHTMLの実体参照を出し、CSSはそれを復号しません。`Element("script")[Raw("…")]` は正しく出るため、除外した要素が能力を失わないという §4.1 の主張は成り立ったままです。破れるのは素の綴りのほうで、無言で破れます。したがってここでの検査は「この要素は子を取れない」ではなく「この要素には `Raw` が要る」と言うことになり、BCF3016とは別の文面を持ちます。

**先頭の改行を落とす `textarea` / `pre`**。パーサは開始タグの直後の改行を1つ捨てます。`appendChild` は捨てません。`Pre["\ntext"]` は prerender で `"text"`、interactive で `"\ntext"` を読みます。`&#xA;` へのエスケープでは回避できません。この規則は文字参照の復号より後に適用されるためです。どちらもcuratedヘルパーです。判定に要るのは子の文字列の先頭1文字であり、形の検査ではなく内容の検査です。コンパイル時に知り得るとも限りません。

**廃止済みのパーサ的void 4タグ**。`param` / `keygen` / `basefont` / `bgsound` は標準の13要素とまったく同じ壊れ方をしますが、意図的にBCF3016の外にあります。§4.1 は検査対象をHTML Living Standardのvoid elementsの一覧として定義しており、集合が誰にも再導出できない列挙ではなく標準に追随するのはそのためです。この4つは除外第6群(標準が取り除いた要素)に含まれ、`Element` 経由でしか到達できません。

### D.2 二項側: (親, 子) の関係を要する破れ

判定に (親, 子) の二項関係を要する形です。§4.1 の境界の外側であり、検査しません。content model表をここで著述して維持することになるためです。二項側には性格の異なる2種類が混ざっており、その混在自体が境界をここに置いた理由の一つです。

**誤った綴りが、パーサに動かされる形。**

| 出力 | パーサが読んだ後 |
| --- | --- |
| `<table><div>x</div></table>` | `<div>x</div><table></table>` |
| `<p><div>x</div></p>` | `<p></p><div>x</div><p></p>`(`<p>` が1個から2個になります) |
| `<table>裸のテキスト</table>` | `裸のテキスト<table></table>` |

`Div[Col]` は子を一つも与えていない状態で食い違います。表の外の `col` は再パースで捨てられるためです。`Element("svg")[Element("b")]` は外来コンテンツの部分木から抜け出します。これらは原理的にはすべて診断で捕まえられます。ただし §4.1 が作らないと述べている (親, 子) のcontent model表を要します。

**正しい綴りが、パーサに正規化される形。** `Table[Tr[Td["x"]]]` は表を書く通常の綴りです。パーサは `tbody` を挿入し、interactive描画は挿入しません。

- prerender: `table > tbody > tr` が一致し、`table > tr` は一致しません
- interactive: `table > tr` が一致し、`table > tbody > tr` は一致しません

どちらかに合わせて書いたスタイルシートは、ハイドレーションで意味が変わります。ここには診断で直せる対象がありません。コードはすでに正しいためです。直す価値があるとすれば、直す場所は発行側かドキュメントです。

### D.3 再現手順

1. `RenderView` が発行するのと同じ `RenderTreeBuilder` のフレームを発行します。
2. `Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRenderer` で描画し、`ToHtmlString()` を読みます。
3. その文字列をブラウザで `innerHTML` に代入し、`appendChild` で組んだツリーと比較します。

描画経路どうしの比較には、Interactive Serverのアプリをホストし、DOMを2回読みます。1回目は `blazor.web.js` を遮断した状態、2回目は `RendererInfo.IsInteractive` が真になった後です。

## 付録E: 畳み込まない値(計測済みの境界)

§2.7 (D) が静的畳み込みから除外する値について、除外の根拠を置きます。いずれも Chromium 上で、畳み込み経路(`AddMarkupContent`)と要素経路(`AddContent` / `setAttribute`)の双方を比較して決めました。§2.7 が定めるのは何を畳むかであり、ここが記録するのはなぜその境界なのかです。

### E.1 マークアップを往復できない4文字

下の2つを見れば、仕様の読解だけでは足りないとわかります。うち1つは仕様上どの段も触れない文字です。

- **復帰(CR)**。パーサはCRLFと単独のCRを、トークン化より前の入力ストリーム前処理でLFへ正規化します。これは `<template>` に対するフラグメントパースでも同じです。一方 `setAttribute` / `createTextNode` は正規化しません。属性値は空白の畳み込みを受けないため `getAttribute` に差が直接現れます(畳み込み経路は `"a\rb"` と `"a\r\nb"` の双方に対し `"a\nb"` を返す)。4つのうち実際に踏みやすいのはこれだけで、CRLFで取得したファイル中の逐語的文字列リテラルはこれを含みます。LFは正規化されないため畳み込み可能です。
- **NUL**。乖離の形が位置によって2つに分かれます。マークアップ経路はテキスト内容ではNULを削除し、属性値では U+FFFD へ置換します。要素経路は双方で保ちます。処理が前処理ではなくトークン化と木構築に分かれて置かれているため、テキスト側と属性側で結果が揃いません。
- **孤立サロゲート**。乖離は無く、保守的な除外です。.NET が描画バッチをUTF-8へエンコードする時点で U+FFFD へ置き換わるため、パーサに届く前に両経路とも U+FFFD になります。仕様上も、入力ストリーム中の孤立サロゲートは parse error であってストリームの書き換えではありません。
- **先頭のU+FEFF**。HTMLのパース段はこれに触れません。ブラウザは描画バッチのフレーム文字列をデコードする際、先頭にあるバイト順マークだけを剥がします。畳み込みは値の位置を動かす操作なので、非畳み込みでは値が自身のフレーム文字列の先頭にあって剥がされ、畳み込みでは `<` で始まるより大きな文字列の内部に入って残ります。したがって除外の条件は文字ではなく位置です。この1つだけはマークアップ経路の方が原文に忠実ですが、畳み込みの契約は「両方の綴りが同じDOMを作る」ことであって、どちらが原文に近いかではありません。

### E.2 非文字列の値と、2つの例外

非文字列を除外する根拠は整形時点のカルチャです。実測では、コンポーネントの `OnInitialized` で `CultureInfo.CurrentCulture` を変えても属性の出力は変わらず、`CultureInfo.DefaultThreadCurrentCulture` を変えると変わります(#158)。

整形そのものは `AddAttribute` の呼び出しの中で終わっており、フレームへ入るのは整形済みの文字列です(2026-08-14に#245で実測)。#158の観察はこれと矛盾しません。`OnInitialized` と `RenderView` が同じスレッドで走るとは限らず、`CultureInfo.CurrentCulture` はスレッドごとだからです。子チャネルも同じです。数値の子を許した場合の解決先は `AddContent(int, object?)` の1つで(`int` / `double` / `decimal` / `DateTime` / enum のいずれもここに決まります)、この呼び出しもその場で `ToString` を呼び、`string?` を渡した場合と同じ `Text` フレームへ整形済みの文字列を積みます。畳み込みへは数値がそもそも到達しません。数値を補間した時点で文字列が定数でなくなるためで、`Span[$"n={3}"]` は畳み込まれず `Span["n=3"]` は畳み込まれます。この2つが意味するのは、本項の除外根拠が数値の子と補間文字列を分けないということです。数値の子の綴りを設けない根拠は別にあります(`DESIGN.md` §4.1、#245)。以上は `NonStringValueFormattingTests` と `ChildValueSpellingTests` が固定します。

**定数 `null`** の一致は #171 で実測しました。要素経路のフレーム層・静的SSR・prerender・interactive初回・両方向の再描画のすべてで属性ごと不在に一致し、`""` とは全段で区別されます。対照としてコンポーネントのパラメータ経路は `null` でもフレームを積むため、省略は要素経路だけの性質です。ただし非畳み込み経路が定数 `null` を書くときは `(global::System.String?)` のキャストを伴います。`AddAttribute` の値位置が多重定義されており、裸の `null` は `string?` と `MulticastDelegate?` のどちらにも決まらずCS0121になるためです(#234で実測)。要素経路もフレームを出さない形にすれば markup と完全に一致しますが、シーケンス番号は発行した呼び出しに対して割り当てられるため、発行する定数 `false` の `bool` と扱いが割れます。

**定数 `bool`** の `true` が `name=""` になることはDOM等価として実測しました。prerender 出力は `=""` の無い裸の `name` を書き、これも同じDOMへパースされます。`false` に対して要素経路はフレームを1つも発行しないため、フレーム数も一致します。

**束縛値はこの節の対象外です**(2026-08-14実測、#307)。カルチャを伴う `.Bind` は属性側を `BindConverter.FormatValue(値, culture:)` で包むため、フレームへ入るのは呼び出しサイトに書かれたカルチャの下で整形済みの文字列です。上の実測はいずれも「整形は呼び出したスレッドのカルチャに従う」ことに帰着しますが、この経路では従いません。スレッドが `de-DE` を持ったまま `CultureInfo.InvariantCulture` を書いた束縛が `1234.5` を積むことを実測しました(`NonStringValueFormattingTests`)。包むかどうかは解決されたオーバーロードがカルチャを取るかだけで決まり、束縛値の型は見ないため、`string` と `bool` の既存経路の出力は1バイトも動きません。

この経路の費用は整形時点ではなくトリムに出ます(2026-08-14実測)。値型を1つでも束縛すると `BindConverter` が丸ごと保持されます。`TrimTestApp` を値型束縛の有無で publish した比較では、`BindConverter` の残存メソッドが28から53へ、`Microsoft.AspNetCore.Components.dll` が71,680から81,408バイトへ増えました(osx-arm64、self-contained、`TrimMode=full`)。残る中にはアプリが束縛しない型の変換器も含まれます(`ConvertToGuidCore` など)。`FormatterDelegateCache` と `ParserDelegateCache` が全変換器を一箇所から参照し、そこに `[DynamicallyAccessedMembers(All)]` と `UnconditionalSuppressMessage` が付いているためです。費用はenum固有ではなく、`string` と `bool` だけを束縛するアプリには生じません。`TrimmedOutputTests` が固定します。

### E.3 掃き出した文字クラス

#150 は E.1 以外の文字クラスを掃き、いずれも両経路で一致することを実測しました(C0制御文字、DEL、NEL、NBSP、U+2028/U+2029、BMPおよび追加面の非文字、内部のBOM、U+FFFD自身、タブ、LF、連続空白、正しい対のサロゲート)。入力ストリーム前処理で書き換えが起きるのは改行正規化だけであり、サロゲート・非文字・制御文字は parse error に分類されるだけでストリームを書き換えません。

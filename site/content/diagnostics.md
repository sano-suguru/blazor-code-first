---
title: Diagnostics
description: Every diagnostic this compiler reports, what each one means, and what to write instead. Search the page for the ID the build printed.
order: 100
group: reference
---

Every diagnostic this compiler reports, what it means, and what to write instead.

The build prints the ID. Search this page for it.

## Your class and its getter

### BCF1001

Error. The class declaring `Body` or `Chrome` is not `partial`, so there is nowhere to emit the
generated `RenderView`.

```csharp
public class Home : BodyComponentBase          // BCF1001
public partial class Home : BodyComponentBase  // what to write instead
```

Only the class that declares the override needs the modifier. A class that merely inherits a
BlazorCodeFirst base without declaring one has nothing generated into it: an intermediate abstract
base, a leaf whose base already declares the override, and a re-abstraction are all left alone.

Without this diagnostic the missing modifier would surface as CS0534 against the abstract
`RenderView`, which names the missing member and never the modifier.

### BCF1002

Error. The expression names something the generated file cannot see, or a `[ViewPart]` method does
not satisfy the static-expansion contract.

```csharp
protected override View Body =>
    Div.Attr("data-found", _rows.TryGetValue(_key, out var row))
       .Attr("title", row.Name);                               // BCF1002: 'row' cannot exist there
```

The generator does not emit an element's parts in the order you wrote them, so a local declared in
one part of the expression does not reach a reference in another. Declare it in a statement ahead of
the return instead.

Two positions do carry a declaration across, because each becomes a header in the generated code
that encloses whatever reads it: an `If` condition scopes over both branches, and a `ForEach` source
over the content and the key.

This is what separates BCF1002 from BCF1003. BCF1003 means the expression could not be sequenced;
BCF1002 means it could, and named something the generated file cannot see.

A `[ViewPart]` body follows the same reserved-name rule [BCF1004](#bcf1004) states for its getter.

### BCF1003

Error. The expression reached the model stage and could not be translated, because it uses a
construct the generator does not analyze.

```csharp
Div[_kids]                    // BCF1003: a child list passed whole
Div[new View[] { … }]         // BCF1003: an explicit array
Div[[..items]]                // BCF1003: a spread
```

Use [`ForEach`](./control-flow.md) for repetition. Everything the generator reads is listed in
[elements and decorations](./elements-and-decorations.md) and [control flow](./control-flow.md):
element helpers, `Component<T>()`, `Fragment`, `Raw`, an inline expression lambda, and a call to a
method marked `[ViewPart]`.

Marking a `View`-returning method of your own `[ViewPart]` keeps the factoring rather than inlining
its markup back into the caller.

An `If` branch and a spliced projection (`.. source.Select(item => …)`) carry their body across
under the author's own names, the same way BCF1004's getter does, so [BCF1004](#bcf1004)'s
reserved-name rule reaches them too.

### BCF1004

Error. The getter does not reach a single returned expression.

```csharp
protected override View Body => Div[H1["Hello"]];              // fine
protected override View Body { get => Div[H1["Hello"]]; }      // fine
protected override View Body { get { return Div[H1["Hello"]]; } }   // fine

protected override View Body { get; } = default;               // BCF1004
```

Locals and expression statements may precede that return. They are copied into the generated
`RenderView` ahead of the calls that emit render-tree frames:

```csharp
protected override View Body
{
    get
    {
        var greeting = $"Hello, {_name}";
        return Div[H1[greeting]];
    }
}
```

A second return and native control flow each need a sequence space of their own, and an auto
property declares no getter body at all. A local declared `__builder` or prefixed `__bcf_` cannot
be declared here either: the generator reserves both spellings everywhere a transplanted local can
land. Rename the local. If the body genuinely cannot be written in this shape, override
`RenderView` by hand: the design-time expression is then unused, and nothing is reported.

BCF1004 reports the declaration, which is what separates it from BCF1003. A class can carry a
missing `partial` and an untranslatable getter at once, and only one is reported at a time: BCF1001
runs first, and adding the modifier is what surfaces BCF1004.

### BCF1005

Error. A nested class declares a design-time expression.

```csharp
public partial class Page
{
    public partial class Row : BodyComponentBase   // BCF1005
    {
    }
}
```

Move the component to a top-level type. Reopening a nested class from the generated file would mean
re-declaring every enclosing type, its type parameters included, so a nested component is rejected
rather than half emitted. Without the diagnostic it would surface as CS0534, which names the missing
member and never the nesting.

## Where the surface is read

### BCF2001

Info. The call cannot be expanded statically, so this area renders through a runtime fragment and
loses its static diff optimization.

The generator expands what it can read: the design-time surface, and `[ViewPart]` methods declared
in this compilation. A call it cannot read is correct and renders correctly. Its frames are rebuilt
rather than diffed against a static template.

A `[ViewPart]` in a referenced project or a NuGet package reaches this, because the generator
collects definitions from the current compilation's syntax and IL carries no body. Reuse across an
assembly boundary belongs in a component.

### BCF3029

Error. Design-time syntax is written where nothing reads it, so it renders nothing and wires up no
handler.

```csharp
private void OnSomething()
{
    // BCF3029: renders nothing, and DoThing is never called
    var card = Div.Class("card").OnClick(DoThing)[Span["hello"]];
}
```

`Html.Div`, `.Class(...)`, `.OnClick(...)` and every other factory and decoration are inert. `View`
is an empty struct, an element helper returns nothing, and a decoration returns its receiver
unchanged. The generator reads the *syntax* you wrote, never the value, and it reads it in three
places: a component's `Body`, a layout's `Chrome`, and the body of a `[ViewPart]` method.

The same API is callable from anywhere, and nothing reads it outside those three places. It
compiles, but it emits no render-tree frames, so nothing is rendered and no event handler is
registered.

Caching a value into a field or property of a design-time type is left alone. Only a local, a
discard, or an argument is reported.

### BCF3030

Error. The call reaches a `View`-returning method that builds from the design-time surface but
carries no `[ViewPart]`, so it renders nothing.

```csharp
private static View Card(string title) => Div.Class("card")[H2[title]];

protected override View Body => Div[Card("Hello")];   // BCF3030
```

Mark the method `[ViewPart]` if it is static, or make it a component. Without the attribute the
method's result carries no fragment, so the call emits no frames.

This is BCF3029 seen from the other side of the call. BCF3029 reports design-time syntax written
where nothing reads it; this reports a call whose callee wrote design-time syntax that nothing read.

### BCF3001

Error. State is mutated inside the design-time expression.

```csharp
protected override View Body
{
    get
    {
        _renderCount++;               // BCF3001
        return Div[Span[$"{_renderCount}"]];
    }
}
```

```csharp
private void OnShown() => _renderCount++;                      // what to write instead
protected override View Body => Div[Span[$"{_renderCount}"]];
```

The getter is a projection of state to UI, and it is translated rather than run. Move the mutation
to an event handler.

Statements ahead of the return are translated too, so a mutation written in one of them is BCF3001
as it always was.

### BCF3015

Error. A type name in a design-time value expression could not be resolved and its spelling depends
on the source file's lexical context.

Values are copied into a generated file that has no `using` directives. Resolved type names are
rewritten as `global::`-qualified names; an unresolved context-dependent one cannot be normalized
safely.

Fix the name, fully qualify it, move a source-generated type to a referenced project, or replace it
with a hand-written C# type. A reference already rooted at `global::` is preserved and left to
ordinary C# resolution. Generic type arguments are checked independently.

## Elements

### BCF3009

Error. The `Element` tag is not a compile-time constant string spelled like a tag name.

```csharp
private const string Widget = "my-widget";

Element(Widget)                  // fine
Element(_kind + "-widget")       // BCF3009: not a constant
Element("")                      // BCF3009: empty
Element("my widget")             // BCF3009: not spelled like a tag name
```

A tag name is an ASCII letter, then ASCII letters, digits, `-`, `_` or `.`.

The constant half keeps the element declarative: a computed tag is neither an injection risk nor a
sequencing problem, but the element no longer names its tag where you wrote it.

The spelling half is a translation break. A tag no element can be named renders as two different
things: prerendering writes it into markup where the HTML parser reinterprets it, while interactive
rendering hands it to `createElement`, which rejects it and ends the circuit.

### BCF1006

Error. A `static ElementView` property used as an element tag alias is declared in a referenced
assembly.

```csharp
// In a referenced project or NuGet package:
public static ElementView MyCard => Element("my-card");

// In this compilation:
MyCard["content"]     // BCF1006
```

The generator resolves an alias by reading its own declaration's syntax — `Element("my-card")` — to
find the tag. IL carries no body, so a declaration reached through a referenced assembly has nothing
to read. Declare the alias in the current compilation instead:

```csharp
static ElementView MyCard => Element("my-card");

MyCard["content"]     // fine
```

A `[ViewPart]` reaches the same wall for the same reason ([BCF1002](#bcf1002)): both features read
their own source, not the target's, so both stop at the compilation boundary.

### BCF3016

Error. Children were written on a void element.

```csharp
Img.Src("/logo.png")["Logo"]     // BCF3016
Element("img")["Logo"]           // BCF3016, same rule
Img.Src("/logo.png").Alt("Logo") // what to write instead
```

The thirteen void elements are `area`, `base`, `br`, `col`, `embed`, `hr`, `img`, `input`, `link`,
`meta`, `source`, `track`, `wbr`.

The children do not survive a round trip through HTML. Prerendering serializes a closing tag the
parser does not accept, so the parser moves the children out and they become the element's
siblings. A stray `</br>` is re-read as a start tag, so `Br["x"]` prerenders as two `<br>`
elements.

Interactive rendering has no parser in between and puts the same children inside. One expression
produces two different DOM trees, and the page changes shape at hydration.

Configure a void element with decorations and put content beside it.

Both spellings are checked, the helper and `Element` with a void tag. Custom elements and unknown
tags are not: `Element("img-viewer")["child"]` is accepted, because there is no standard to read
their content model out of.

This is the limit of what the surface checks about HTML, and the limit is deliberate. BCF3016 is
decidable from the element tag by itself. Whether a particular child is allowed inside a particular
parent is not, so `Table[Div["x"]]` is accepted along with attributes an element does not define.

### BCF3027

Error. A declaration of your own took the simple name the element helper wanted.

```csharp
[Parameter] public string Data { get; set; }
Div[Data["Heading"]]                          // BCF3027, a member

public sealed class Table;                    // Table["x"]   — BCF3027, a type
namespace MyApp.Article { }                   // Article["x"] — BCF3027, a namespace
private string Summary() => "";               // Summary["x"] — BCF3027, a method

Div[Html.Data["Heading"]]                     // what to write instead
```

`using static BlazorCodeFirst.Html;` imports every conforming HTML element name, and a declaration
of your own wins simple-name lookup over an imported one. Blazor parameters named `Label`, `Data`,
`Summary` or `Source` are ordinary, so this happens.

C# has an error for every one of these — CS1503 on the index argument, CS0119, CS0118, CS0021 — and
none of them is reported. As long as the body does not translate, the component has no generated
`RenderView`, so the compiler stops before it binds method bodies, which is where all four are
found.

## Decorations

### BCF3008

Error. A decoration was applied to something that opens no element.

```csharp
If(_open, () => Div["x"]).Class("card")   // BCF3008
Div["text"].Class("card")                 // BCF3008: the brackets already produced a View
Div.Class("card")["text"]                 // what to write instead
```

A decoration folds into the owning element's attributes, so it needs an element to attach to.
`If`, `ForEach`, `Fragment`, `Raw`, and a `[ViewPart]` or component result open none.

### BCF3026

Error. The name written in a decoration's position is not one this library declares.

```csharp
Div.Clas("card")     // BCF3026
Div.Class("card")    // what to write instead
```

A misspelling reaches this, and so does an extension method of your own that takes an element and
gives one back. C# has an error for the misspelling, and the same declaration-stage stop prevents it
being reported.

### BCF3010

Error. An attribute or event is bound more than once on one element.

```csharp
Input.Type("text").Attr("value", _a).Attr("value", _b)   // BCF3010
Input.Type("text").Attr("value", _b)                     // what to write instead
Div.Class("card").Class("is-open")                       // fine: class folds
```

Two bindings in the attribute channel discard the earlier one, because the last write wins. One
name bound through the attribute channel and once through the event channel keeps both, so an inline
handler and a C# handler each fire on every event. Neither is what you wrote.

`class` is the sole exception: `.Class` and `.Attr("class", …)` fold into one space-joined
attribute. `style` is not an exception, so two of them on one element is BCF3010.

### BCF3011

Error. The `.Attr` name or `.On` event name is not a non-empty compile-time constant string.

```csharp
Div.Attr(_name, "x")          // BCF3011
Div.Attr("data-kind", "x")    // what to write instead
```

The name lowers to a literal, and holding it to a constant is also what makes class folding and
duplicate-binding detection possible.

### BCF3023

Error. A decoration written on `class` carries a value the class channel cannot join as text.

```csharp
Div.Attr("class", _selected)                     // BCF3023
Div.Attr("class")                                // BCF3023: a presence has no text
Div.Class(_selected ? "is-selected" : null)      // what to write instead
```

The class channel joins its decorations into one value as text, so `class` takes a string and
nothing else. The `bool` overload of `.Attr` is Blazor's conditional-attribute form, and the bare
`.Attr(name)` spelling stands for a presence; neither is text.

The value would otherwise translate two different ways. With one class decoration on the element the
channel emits the value alone, so `true` renders `class=""` and empties the class list. With two or
more the channel joins them with `+`, so the same `true` renders `class="a True"`. One spelling
meaning two things depending on a count written elsewhere in the chain is the defect.

### BCF3024

Error. One element carries both a class-channel decoration and a `.Bind` on `class`.

```csharp
Div.Class("card").Bind("class", "onchange", () => _classes)   // BCF3024
```

`.Class` and `.Attr("class", …)` fold into a single attribute. A `.Bind` on the same name does not
join that fold; it emits its own frame, so the element is emitted with `class` twice.

Which one survives has no single answer: prerendered markup is resolved by the HTML parser, which
keeps the first, while an interactive render applies them through the DOM, where the last write
stands.

Supply the whole class value from one place. Bind it alone and let the getter carry everything, or
drop the binding and use the decorations.

### BCF3033

Error. The same non-attribute decoration is written twice on one node.

```csharp
Div.Key(row.Id).Key(row.Slug)["x"]   // BCF3033
Div.Key(row.Id)["x"]                 // what to write instead
```

`.Key` and its siblings each occupy a channel that holds one value. All three break differently, and
none of them breaks visibly:

- `SetKey` writes into the open frame, so the second call overwrites the first.
- `AddComponentRenderMode` appends, and the renderer reads the first frame it finds, so there the
  second one is ignored.
- A reference capture appends too, and both actions run.

Write the decoration once, with the value the node should carry. Two candidate keys mean the
identity is not decided, and the source is the only place where deciding it is visible.

### BCF3034

Error. The component's own declaration fixes its render mode, so the call site cannot set one.

```csharp
Component<Counter>().RenderMode(RenderMode.InteractiveWebAssembly)   // BCF3034 if Counter declares one
```

The framework rejects the pair: `ComponentFactory` throws when a type carrying a
`RenderModeAttribute` also receives a caller-specified mode. The call-site form exists for a
component that declares no mode of its own, which is the case where it is needed: the same component
rendered interactively from one page and statically from another.

Drop the `.RenderMode` at the call site and let the component's own attribute apply. If the mode
genuinely has to vary by caller, remove the attribute from the component instead, and then every
call site must name a mode.

### BCF3039

Error. `.FormName`'s argument is a literal empty string or `null`.

```csharp
Form.FormName("")["submit"]      // BCF3039
Form.FormName("save")["submit"]  // what to write instead
```

`.FormName` lowers to `AddNamedEvent("onsubmit", name)`, and the framework throws at run time for
either shape: `ArgumentException` for an empty name, `ArgumentNullException` for a null one. A
runtime expression is not required to be a compile-time constant — only a literal known ahead of
time to always throw is rejected here.

### BCF3040

Error. `.FormName` is written on an element whose tag is not `form`.

```csharp
Div.FormName("save")["submit"]    // BCF3040
Form.FormName("save")["submit"]   // what to write instead
```

`.FormName` lowers to `AddNamedEvent("onsubmit", …)`, and `onsubmit` is a browser-native event that
only ever fires on a `<form>` element. A registration on any other tag is never reached.

## Events

### BCF3019

Error. The event name does not begin with `on`.

```csharp
Input.On("input", (ChangeEventArgs e) => …)     // BCF3019
Input.On("oninput", (ChangeEventArgs e) => …)   // what to write instead
```

Blazor's event attribute names always begin with `on`, and the prefix is never added for you. A name
without it reaches `AddAttribute` as an ordinary attribute, so the handler is never called and
nothing reports it at runtime.

On `.Bind` this does a second job. The attribute name and the event name are adjacent string
arguments, so swapping them compiles; this is what stops a swapped pair.

### BCF3028

Error. The handler's argument type is not one the named event delivers.

```csharp
Button.On("onclick", (MouseEventArgs e) => Zoom(e.ClientX, e.ClientY))["Zoom"]   // the delivered type
Button.On("onclick", (EventArgs e) => Save())["Save"]                            // a base of it: fine
Button.On("onclick", (KeyboardEventArgs e) => Save())["Save"]                    // BCF3028
```

Blazor dispatches an event by casting its argument object to the handler's argument type, so a base
of the delivered type is accepted and a sibling is not. A type that is not an `EventArgs` at all is
the same diagnostic.

The mapping is read from the `[EventHandler]` metadata the framework ships and from any registration
in the compilation being built. An event with no entry has no mapping and is not checked:

```csharp
[EventHandler("onrate", typeof(RatingEventArgs))]
public static class AppEventHandlers;
```

### BCF3035

Error. An event modifier has no event before it on the element.

```csharp
Form.PreventDefault().On("onsubmit", () => Save())   // BCF3035
Form.On("onsubmit", () => Save()).PreventDefault()   // what to write instead
```

`.PreventDefault` and `.StopPropagation` attach to the event written before them, which is the only
reading a chain offers: the decorations carry no event name of their own.

### BCF3036

Error. The same event modifier is written twice for one event.

```csharp
Form.On("onsubmit", () => Save()).PreventDefault().PreventDefault()   // BCF3036
Form.On("onsubmit", () => Save()).PreventDefault()                    // what to write instead
```

One of the two has no effect whichever way the model takes it, and which one is not visible at the
call site.

Write the modifier once. It is a flag rather than a value, so a second one requests nothing the
first did not already do.

### BCF3038

Error. The event's own `[EventHandler]` registration disables that modifier.

Blazor gates each modifier per event, and the renderer ignores an attribute the registration
disabled. Remove the modifier; the event itself is correct and stays.

## Control flow

### BCF3002

Warning. The `ForEach` key selector does not reference its item, so it cannot identify one.

```csharp
ForEach(rows, key: r => 0, content: r => Li[r.Name])          // BCF3002
ForEach(rows, key: r => r.Id, content: r => Li[r.Name])       // what to write instead
```

A key derived from the item is what lets Blazor preserve per-row state across insertion, removal,
and reordering. A constant, an external index, or another list's item forces a full re-render.

The check is deliberately conservative: it does not detect an item-derived key that is index-like in
practice.

### BCF3003

Error. The `ForEach` content root is not a single element or component, so the key has nothing to
attach to.

```csharp
ForEach(rows, key: r => r.Id, content: r => If(r.Visible, () => Li[r.Name]))   // BCF3003
ForEach(rows, key: r => r.Id, content: r => Li[If(r.Visible, () => Span[r.Name])])   // what to write instead
```

The key is applied to the content root's frame, and `SetKey` keys the currently open element or
component frame. A bare `If`, a nested `ForEach`, bare text, a `Fragment`, a `Raw`, and an
externally supplied `RenderFragment` all open no single keyable frame.

Wrap the content in a container element. A `ForEach` that declines its key with `key: null` emits no
`SetKey`, so those roots are allowed there.

### BCF3004

Error. The `ForEach` key or content has a shape the generator cannot sequence.

The key body is copied into the `SetKey` call, so it has to be an expression. The content is given
one static sequence space that every iteration reuses, which a second return or a native `foreach`
would each need their own copy of.

Content accepts an expression lambda, a block with one trailing `return`, a block ending in a native
`if`/`else` ([BCF2002](#bcf2002)), a block ending in a native `switch` ([BCF2002](#bcf2002)), and a
single-parameter `View`-returning method group. `foreach` is not accepted.

The key body and the content body follow the same reserved-name rule [BCF1004](#bcf1004) states
for its getter.

### BCF2002

Info. A native `if`/`else` or `switch` degrades to a dynamic region.

```csharp
protected override View Body
{
    get
    {
        if (_flag)                 // BCF2002: correct, but not statically assigned
        {
            return Span["yes"];
        }
        else
        {
            return Span["no"];
        }
    }
}
```

A native `if`/`else` or `switch` can be written as the last statement of a `Body`/`Chrome` getter, a
`ForEach` content lambda, or a `[ViewPart]` body. It is transplanted whole into a region whose
boundary sequence is fixed to syntactic position — the same shape `If()` uses. Unlike `If()`, each
arm's or section's content renders through a freshly synthesized `RenderFragment` rather than a
statically assigned sequence range. Only one arm or section ever executes, so no static width can be
reserved for content that might not run. Correctness is unaffected; the frames for whichever one runs
are rebuilt rather than diffed against a static template.

Reported once per `if`/`else` chain or `switch`, at the outermost `if` or at the `switch`'s
discriminant, regardless of how many `else if` links or `case` sections it holds. A `switch` section's
own `return` is what closes it; each section still needs an explicit `break` in the generated code so
control does not fall through into the next one. `foreach` is not yet accepted at this position and
remains [BCF1004](#bcf1004)/[BCF3004](#bcf3004)/BCF1002.

### BCF3032

Error. The `ForEach` content root writes its own `.Key` while the loop also applies one.

```csharp
ForEach(rows, key: r => r.Id, content: r => Li.Key(r.Id)[r.Name])   // BCF3032
```

Two `SetKey` calls land on one frame, of which the second wins, so which key is authoritative
depends on emission order rather than on anything at the call site. Key the root or key the loop.

## Components

### BCF3005

Error. The parameter selector is not a simple property selection on its own lambda parameter.

```csharp
Component<Card>().Param(c => (string)c.Label, "x")   // BCF3005
Component<Card>().Param(c => c.Label, "x")           // what to write instead
```

The generator reads the parameter's name out of the selector's spelling, so a cast, a method call, a
null-conditional access, or a member of a captured variable has no name to read. `.Param`,
`.Template`, and `.Bind` all take that same selector.

### BCF3006

Error. The selected property is not a settable `[Parameter]`.

```csharp
public string Label { get; set; }              // BCF3006: no [Parameter]
[Parameter] public string Label { get; }       // BCF3006: no setter

[Parameter] public string Label { get; set; }  // what to write instead
```

Only a property marked `[Parameter]` with an accessible setter can be bound. Setting anything else
would throw when Blazor applies the parameters.

Mark the property `[Parameter]` and give it an accessible setter. A value the component should not
receive from its caller belongs in a field rather than in a parameter.

### BCF3007

Error. The chain binds the same parameter more than once.

```csharp
Component<Card>().Param(c => c.Label, "a").Param(c => c.Label, "b")   // BCF3007
Component<Card>().Param(c => c.Label, "b")                            // what to write instead
```

Every channel counts: `.Param`, `.Template`, `.Bind`, and child content written in brackets. Blazor
applies the last write, so the earlier value is discarded.

Bind the parameter once, with the value the component should end up with. A value that depends on
state belongs in the expression you pass, not in a second `.Param`.

### BCF3012

Error. The type argument did not resolve while the generator ran.

The usual cause is a `.razor` component declared in the same project. The Razor compiler is itself a
source generator, and source generators cannot observe each other's output, so the type is
unresolved here even though it exists in the final compilation.

Move it to a referenced project, write it as a hand-authored C# component, or fix the name. The same
component in a referenced project or a NuGet package resolves normally.

A typo, an inaccessible type, an ambiguous name, or a missing `using` reaches this too, and a C#
resolution error is reported at the same position.

### BCF3013

Error. Child content was written in brackets on a component that cannot receive it.

```csharp
Component<Panel>()["body"]                                  // BCF3013 if Panel has no ChildContent
Component<Panel>().Param(c => c.Content, Span["body"])      // what to write instead
```

Brackets bind to a parameter named `ChildContent`, mirroring how Razor lowers nested content. It has
to be a settable `[Parameter]` typed `RenderFragment` or `RenderFragment<TContext>`; the generic one
receives the children with its context discarded.

### BCF3014

Error. An inert design-time value was passed to the generic `Param`.

```csharp
Component<Card>().Param(c => c.Body, Div["x"])              // BCF3014
Component<Card>().Param(c => c.Body, () => Div["x"])        // what to write instead
```

`View` and `ComponentView<T>` are empty markers read by the generator, not runtime values. The
generic `Param` emits its value expression verbatim, so binding one assigns the marker: an
`object`-typed parameter accepts it with no exception and renders wrong output, while a typed
parameter throws an invalid cast when Blazor applies parameters.

### BCF3022

Error. The contextual `.Template` content is not an inline expression lambda.

```csharp
Component<Grid<Row>>().Template(c => c.RowTemplate, RenderRow)          // BCF3022
Component<Grid<Row>>().Template(c => c.RowTemplate, row => Td[row.Name]) // what to write instead
```

A method group, an anonymous method, and a block-bodied lambda all hide the content behind a call,
leaving no expression to sequence and no parameter symbol to substitute the generated context
variable for.

The content follows the same reserved-name rule [BCF1004](#bcf1004) states for its getter.

### BCF3025

Error. `Slot` is written outside the body of a content-taking `[ViewPart]`, or named other than
exactly once.

```csharp
[ViewPart]
public static SlotView Panel(View heading) =>
    Section[heading, Div.Class("body")[Slot]];   // fine

protected override View Body => Div[Slot];       // BCF3025: Body receives no brackets
```

`Slot` marks where a part places the content its caller supplied in brackets, so it means nothing
where there is no caller content: a component's `Body` or `Chrome` receives no brackets, and a part
returning `View` is called without them.

A part that takes content declares `SlotView` as its return type and names `Slot` exactly once.
Naming it twice would emit the caller's content twice; not naming it at all would discard content
the caller was required to supply.

### BCF3042

Error. `.Class`/`.Attr`, or one of the `Id`/`Type`/`Title`/`Role`/`Href`/`Src`/`Alt` shortcuts, on a
component call names, case-insensitively, a parameter the component declares.

```csharp
Component<Card>().Attr("label", "hi")           // BCF3042: Card declares [Parameter] Label
Component<Card>().Param(c => c.Label, "hi")     // what to write instead
```

Blazor matches an attribute name against a component's declared parameters case-insensitively, so
`"label"` would otherwise silently set `Label` at runtime instead of landing in
`AdditionalAttributes` — bypassing `.Param`'s type checking and giving no signal that it happened.

Bind the parameter with `.Param` instead, so the value is checked against its declared type.

## Two-way binding

### BCF3017

Error. The `.Bind` getter is not an inline lambda with an expression body.

```csharp
Input.Bind("value", "oninput", GetName)          // BCF3017
Input.Bind("value", "oninput", () => _name)      // what to write instead
```

The getter's body is copied into two places, once as the bound attribute's value and once as the
binder's current value, so it has to be available as an expression. A block-bodied lambda and a
method group both hide it behind a call.

The setter argument carries no such restriction, because it is handed to `EventCallback` whole and
never taken apart.

### BCF3018

Error. The getter-only `.Bind`'s getter body cannot be assigned to, so no setter can be derived.

```csharp
Input.Bind("value", "oninput", () => Name.Trim())                          // BCF3018
Input.Bind("value", "oninput", () => Name, v => Name = v.Trim())           // what to write instead
```

The getter-only form derives its setter by placing the getter's body on the left of an assignment,
so that body has to be a field, a settable property, or an element access whose indexer has a
setter.

A setter existing is not the same as the derived assignment being able to call it. The derived
setter is a lambda, so an `init` accessor is out of reach: C# admits one only in an object
initializer, a constructor, or another `init` accessor. A setter declared narrower than the property
that carries it is out of reach too, this time depending on where the assignment lands.
`{ get; private set; }` on the component itself is accepted, because the generated `RenderView` is
emitted into a partial of that same class; the same property on another type is not.

A local, a parameter, and a `ForEach` iteration variable are rejected even though C# would assign to
them: the design-time expression is a property getter, so those go out of scope with each render and
the write-back would not survive to the next one. A member of an iteration variable is accepted,
because it writes through to the element in the source list.

### BCF3020

Error. The component declares no matching change callback, so a two-way binding has nowhere to write
back.

```csharp
Component<Field>().Bind(c => c.Value, () => _query)   // BCF3020 without ValueChanged
Component<Field>().Param(c => c.Value, _query)        // what to write instead, one way
```

Component binding derives its parameter names rather than taking them from you, which is the
opposite of the element surface. That is sound only because the derivation can be checked: the
component's type is known, so the generator looks up `{name}Changed` and reports this when it is
absent or carries the wrong type.

Element binding has no such check available, which is why it makes you write both names.

### BCF3031

Error. `.Bind` writes a format for a value type the framework declares no format-taking converter
for.

```csharp
Input.Bind("value", "oninput", () => _count, format: "N0")   // BCF3031
```

`BindConverter.FormatValue` and `CreateBinder` declare their format-taking overloads for `DateTime`,
`DateTimeOffset`, `DateOnly`, `TimeOnly` and their nullable forms only. A format on anything else
would leave the generated file with a call that does not bind, and that C# error is raised inside
generated code rather than in the source you wrote.

Drop the format, or format the value in the getter and parse it in an explicit setter. The set is
read from the framework's own metadata rather than enumerated by this compiler, and the culture is
not in question: every type this surface binds may carry one.

## Scoped CSS

### BCF3041

Error. A `Foo.cs.css` file has no matching `Foo.cs`, and `Foo.cs` declares neither a component nor a
`[ViewPart]` method.

```
Counter.cs.css   // scopes nothing: no Counter.cs in this project   // BCF3041
```

A `.cs.css` file's scope is discovered by matching its name against a `.cs` file — there is no
explicit way to pair the two, unlike Razor's `ScopedCssInput`. An unmatched file is therefore always
a mistake, most often a typo in the file name, so it is reported rather than silently dropped.

Rename the `.cs.css` file to match the component (or the file declaring the `[ViewPart]` methods)
whose elements it is meant to scope, or add that file if it does not exist yet.

## Next

Back to [getting started](./getting-started.md), or read the [element
vocabulary](./elements-and-decorations.md).

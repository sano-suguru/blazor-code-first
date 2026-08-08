---
title: Two-Way Binding
order: 60
---

A two-way binding is one decoration that writes a value out to the DOM and reads the user's edit back
into your state. `.Bind` is Razor's `@bind`, spelled so that everything it needs is an argument you can
see. The generator lowers it to an attribute frame, an event frame carrying Blazor's own
`CreateBinder`, and — when the bound attribute is `value` or `checked` — the DOM resynchronization
that keeps the element honest. No reflection, and no expression tree compiled at runtime.

## Binding an element

Name the attribute that carries the value, the event that reports a change, and a lambda that reads
the current value:

```csharp
public partial class NameField : BodyComponentBase
{
    private string _name = "";

    protected override View Body =>
        Div[
            Input.Type("text").Bind("value", "oninput", () => _name),
            P[$"Hello, {_name}"]];
}
```

The lambda is read in both directions. Its body becomes the attribute's value, and — in this
getter-only form — the same body becomes the left-hand side of the assignment the generator writes
for you. So the target has to be assignable: a field, a property with a setter, or a path through
either (`_form.Name`, `Model.Items[0].Title`, `_dict["k"]`). A computed expression such as
`() => _name.ToUpper()` reports BCF3018, and the way to write that is the explicit setter below.

Use `"oninput"` to bind on every keystroke and `"onchange"` to bind when the element loses focus. Those
are the two you will usually want, and no list restricts the pair: both names have to be non-empty
compile-time constants (BCF3011), the event name has to start with `on` (BCF3019), and neither may
already be bound on the same element by another decoration (BCF3010). Nothing checks the names against
HTML.

## Why you write both names

Razor infers the attribute from the markup: it reads the literal `type="checkbox"` out of your `.razor`
file and binds `checked` instead of `value`. This surface has no literal to read. The tag is a string
and `type` is an expression — `Input.Type(kind)` is an ordinary C# call whose value may not be known
until it runs — so there is nothing an inference could be checked against.

Defaulting to `value` would then produce the one failure worth going out of the way to avoid: a
checkbox bound to the wrong attribute, silently, with no diagnostic to tell you. So the rule across
the whole surface is **infer only what you can verify**. The element side cannot verify, so it does
not infer, and you write two short strings instead.

The half of the mistake that *is* checkable is caught: an event name that does not start with `on`
reports BCF3019, so swapping the two arguments stops at compile time rather than adding a dead
attribute.

The component side of `.Bind` does infer names, because the same rule allows it there — see
[binding a component parameter](#binding-a-component-parameter).

## Checkboxes bind `checked`

A checkbox binds a `bool` to the `checked` attribute, on `onchange`:

```csharp
private bool _agreed;

protected override View Body =>
    Label[
        Input.Type("checkbox").Bind("checked", "onchange", () => _agreed),
        " I agree"];
```

`bool` is HTML's boolean-attribute form, the same one
[`.Attr` takes](./elements-and-decorations.md#decorations): `true` renders the attribute with an empty
value, `false` leaves it out entirely.

## Normalizing with an explicit setter

A fourth argument replaces the generated assignment with your own setter, which is where validation,
normalization, or any work that has to happen on each edit belongs:

```csharp
private string _name = "";

protected override View Body =>
    Input.Type("text").Bind("value", "oninput", () => _name, v => _name = v.Trim());
```

This covers what Razor splits across `@bind:get` / `@bind:set` and `@bind:after`: the setter *is* the
write, so anything you would have run afterwards goes in the same lambda. Note that the write is now
yours to perform — nothing assigns for you once you supply a setter. A method group works too
(`SetName`), the lambda may have a block body, and an `async` variant is available by returning
`Task`:

```csharp
Input.Type("text").Bind("value", "oninput", () => _query, async v =>
{
    _query = v;
    await SearchAsync(v);
});
```

Only the getter-only form needs an assignable getter. With a setter, the getter is only ever read, so
any expression will do.

A normalizing setter creates a divergence: the element shows what was typed, while your field holds
the trimmed value. Ordinary diffing writes nothing, because the render tree has not changed since the
last render. On a `value` or `checked` binding, `.Bind` registers that attribute for DOM
resynchronization to close the gap, so the element is corrected to show the normalized value. You get
this without asking for it.

Those two names are the whole of it. Blazor's client sends back a form element's own `value` — or
`checked` for a checkbox — and nothing else, so those are the only two names the generator registers.
A binding to any other attribute registers nothing; `.Bind("hue", "onhuechange", () => _hue, Normalize)`
on a custom element is the usual shape. The setter still runs and the new value still reaches the DOM
by ordinary diffing on the next render. What is missing is the repair above, for the case where
normalizing leaves the render tree unchanged and the element goes on showing what was typed.

The setter receives `""` for an emptied text input, never `null` — which is why it takes a
non-nullable `string`. Writing to your own state from the setter is allowed, even though writing to
state anywhere else in a `Body` reports BCF3001: a setter is a deferred handler, like an `.OnClick`
lambda, and does not run while the tree is being built.

## Values that are not `string` or `bool`

Those two types are all an element binding takes. Anything else would be formatted on its way to the
DOM under the culture of whichever thread does the formatting, which is not the one your component ran
under, and Razor answers that by picking a culture from the element's literal `type` — the literal this
surface does not read.

So write the conversion yourself, on both sides, where the culture is visible:

```csharp
private decimal _amount;

protected override View Body =>
    Input.Type("text").Bind(
        "value", "onchange",
        () => _amount.ToString(CultureInfo.InvariantCulture),
        v => _amount = decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : _amount);
```

There is deliberately no shorter spelling. A shorter one would have to choose a culture on your
behalf, and choosing it out of sight is the thing being avoided.

## Binding a component parameter

On a component the names are derived rather than written, because there the derivation can be checked.
`.Bind` selects the parameter with a lambda and the generator appends `{Name}Changed`, plus
`{Name}Expression` when the component declares it:

```csharp
using Microsoft.AspNetCore.Components.Forms;

private readonly NameModel _model = new();

protected override View Body =>
    Component<InputText>().Bind(c => c.Value, () => _model.Name);
```

That single call supplies `Value`, `ValueChanged`, and `ValueExpression`. Every derived name is looked
up on the component type, so a missing or mistyped `{Name}Changed` reports BCF3020 rather than binding
nothing. This is what makes the asymmetry with the element side a rule and not an inconsistency.

`{Name}Expression` is the reason the target is written as a getter lambda rather than passed by
reference. A component under an `EditForm` resolves a `FieldIdentifier` from that expression, and the
identifier is what ties the input to a property of your model, so validation messages land on the right
field. No other spelling could supply it.

The `EditForm` itself is the part you cannot yet write here. `EditForm.ChildContent` is a
`RenderFragment<EditContext>`, and `.Param` takes BlazorCodeFirst content only for a non-generic
`RenderFragment`, so `Component<EditForm>()[…]` reports BCF3013. Until
[issue #161](https://github.com/sano-suguru/blazor-code-first/issues/161) closes, put the bound inputs
in their own component and hand that to `EditForm.ChildContent` as a `RenderFragment<EditContext>`
written by hand — the binding above, and the `ValueExpression` it supplies, are unaffected.

An explicit setter and an `async` setter are available here too, with the same meaning as on an
element. `TValue` is not restricted to `string` and `bool`, because the value goes to a parameter
rather than to the DOM and nothing formats it on the way.

Remember that `Component<T>()` cannot name a `.razor` component declared in the same project
([BCF3012](./components-and-reuse.md#calling-an-existing-razor-or-third-party-component)). Framework
components such as `InputText`, and hand-written C# components, always resolve.

## What is checked

- **BCF3017** — the getter is not an inline lambda with an expression body, such as a block-bodied
  lambda or a method group. Its body has to be extractable, because it is copied into both the
  attribute value and the binder. The setter has no such restriction; it is only handed over.
- **BCF3018** — the getter-only form's getter body is not assignable. Calls and operators, get-only
  properties, `readonly` fields, and locals or `ForEach` iteration variables themselves are rejected;
  a *member* of an iteration variable (`o.Title`) is fine, since writing it changes the underlying
  item. Write an explicit setter instead.
- **BCF3019** — the event name does not start with `on`. Such a name would be added as a plain
  attribute and the handler would never fire, so this is what catches the two names swapped.
- **BCF3020** — the component's `{Name}Changed` parameter is missing or is not an
  `EventCallback<TValue>`.

An element may carry more than one `.Bind`. If two of them share an attribute name or an event name,
that is BCF3010, the same duplicate any two decorations would report. DOM resynchronization — the
repair that puts a normalized value back over what the user typed — applies to `value` and `checked`
only, because those are the only two the browser sends back with the event.

## Next

See [elements and decorations](./elements-and-decorations.md#decorations) for the one-way `.Attr` and
`.On` this is built from, or [components and reuse](./components-and-reuse.md) for `.Param` and the
rest of the component surface.

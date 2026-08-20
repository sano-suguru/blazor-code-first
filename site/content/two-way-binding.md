---
title: Two-Way Binding
description: .Bind is the Razor bind directive with every argument visible: the attribute, the event, and a lambda reading the current value.
order: 80
group: write
---

A two-way binding is one decoration that writes a value out to the DOM and reads the user's edit back
into your state. `.Bind` is Razor's `@bind`, spelled so that everything it needs is an argument you can
see. Nothing generated compiles an expression tree at runtime, and nothing generated reflects; binding
an enum reaches one reflective lookup inside the framework's own converter.

## Binding an element

Name the attribute that carries the value, the event that reports a change, and a lambda that reads
the current value. Only the first of those three appears in the markup:

<!-- bcf-figure: TextBinding -->

```csharp
protected override View Body =>
    Div[
        Input.Type("text").Bind("value", "oninput", () => _name),
        P[$"Hello, {_name}"]];
```

```html
<div>
    <input type="text" value="Ada">
    <p>Hello, Ada</p>
</div>
```

The lambda is read in both directions. Its body becomes the attribute's value, and — in this
getter-only form — the same body becomes the left-hand side of the assignment the generator writes
for you. So the target has to be assignable: a field, a property with a setter, or a path through
either (`_form.Name`, `Model.Items[0].Title`, `_dict["k"]`). A computed expression such as
`() => _name.ToUpper()` reports [BCF3018](./diagnostics.md#bcf3018), and the way to write that is the
explicit setter below.

Use `"oninput"` to bind on every keystroke and `"onchange"` to bind when the element loses focus.
Those are the two you will usually want, and no list restricts the pair. Three rules do: both names
have to be non-empty compile-time constants ([BCF3011](./diagnostics.md#bcf3011)), the event name has
to start with `on` ([BCF3019](./diagnostics.md#bcf3019)), and neither may already be bound on the same
element by another decoration ([BCF3010](./diagnostics.md#bcf3010)).

Nothing checks the names against HTML.

## Checkboxes bind `checked`

A checkbox binds a `bool` to the `checked` attribute, on `onchange`. The same decoration, a different
first argument, and a different attribute in the output:

<!-- bcf-figure: CheckboxBinding -->

```csharp
protected override View Body =>
    Label[
        Input.Type("checkbox").Bind("checked", "onchange", () => _agreed),
        " I agree"];
```

```html
<label><input type="checkbox" checked> I agree</label>
```

`bool` is HTML's boolean-attribute form, the same one
[`.Attr` takes](./elements-and-decorations.md#decorations): `true` renders the attribute with an empty
value, `false` leaves it out entirely.

## Why you write both names

Razor infers the attribute from the markup: it reads the literal `type="checkbox"` out of your `.razor`
file and binds `checked` instead of `value`. This surface has no literal to read. The tag is a string
and `type` is an expression — `Input.Type(kind)` is an ordinary C# call whose value may not be known
until it runs — so there is nothing an inference could be checked against.

Defaulting to `value` would produce the failure most worth avoiding: a checkbox bound to the wrong
attribute, silently, with no diagnostic. So the rule across the whole surface is **infer only what
you can verify**. The element side cannot verify, so it does not infer, and you write two short
strings instead.

The half of the mistake that *is* checkable is caught: an event name that does not start with `on`
reports [BCF3019](./diagnostics.md#bcf3019), so swapping the two arguments stops at compile time
rather than adding an attribute that does nothing.

The component side of `.Bind` does infer names, because the same rule allows it there — see
[binding a component parameter](#binding-a-component-parameter).

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
any expression will do. The getter itself must be an inline lambda with an expression body either way
([BCF3017](./diagnostics.md#bcf3017)), because its body is copied into both the attribute value and
the binder.

A normalizing setter creates a divergence: the element shows what was typed, while your field holds
the trimmed value. Ordinary diffing writes nothing, because the render tree has not changed since the
last render. On a `value` or `checked` binding, `.Bind` registers that attribute for DOM
resynchronization to close the gap, so the element is corrected to show the normalized value. This
needs no configuration.

That registration covers two names only. Blazor's client sends back a form element's own `value` — or
`checked` for a checkbox — and nothing else, so those are the only two names the generator registers.
A binding to any other attribute registers nothing; `.Bind("hue", "onhuechange", () => _hue, Normalize)`
on a custom element is the usual shape. The setter still runs and the new value still reaches the DOM
by ordinary diffing on the next render. What is missing is the repair above, for the case where
normalizing leaves the render tree unchanged and the element goes on showing what was typed.

The setter receives `""` for an emptied text input, never `null` — which is why it takes a
non-nullable `string`. Writing to your own state from the setter is allowed, even though writing to
state anywhere else in a `Body` reports [BCF3001](./diagnostics.md#bcf3001): a setter is a deferred
handler, like an `.OnClick` lambda, and does not run while the tree is being built.

## Numbers, dates, and enums

Any type binds, as long as you write the culture. It is the last argument and it cannot be omitted:

```csharp
private int _age;

protected override View Body =>
    Input.Type("number").Bind("value", "oninput", () => _age, CultureInfo.InvariantCulture);
```

The culture formats the value on the way out and parses it on the way back, through Blazor's own
`BindConverter`. Numbers, dates, times, `Guid`, enums, and every nullable form of those all work,
because the conversion is the framework's rather than this library's.

It is an argument rather than a default because a default would be a culture chosen without your
seeing it. Razor picks one from the element's literal `type` — the literal this surface does not
read, for the same reason it does not infer the attribute name. The choice moves to the call site.

### Write the invariant culture for `number` and `date`

`<input type="number">` and `<input type="date">` are defined in terms of a fixed format, not the
user's locale. Binding either under `CultureInfo.CurrentCulture` produces a value the element rejects
as soon as the current locale writes decimals with a comma.

**This is not diagnosed.** The check would need to read `type`, and `type` is an expression here. A
rule that fired only on a literal would catch the mistake in one spelling and miss it in another.
Write `CultureInfo.InvariantCulture` for those two, and use the current
culture for text the user reads as prose.

### A value that will not parse is put back

If the converter cannot read what was typed, your setter is never called and both your field and the
element return to the previous value. That is Blazor's behaviour, and `.Bind` reaches it through the
same DOM resynchronization described above.

This has a consequence worth choosing deliberately. On `"oninput"` the reversion runs on every
keystroke, so a decimal point typed into an `int` binding does not survive: `4.` is rejected and the
`.` is removed. For numeric input that is usually not what you want:

```csharp
// Reverts on blur, so a half-typed number survives.
Input.Type("number").Bind("value", "onchange", () => _amount, CultureInfo.InvariantCulture);
```

`"oninput"` is still right when every intermediate value is meaningful — a range slider, or a text
field whose type accepts anything the user can type.

Emptying the field is a separate matter, and it is not a rejection. Blazor reads an empty string as
the type's default, so clearing an `int` binding gives you `0` rather than leaving the previous value
in place. Bind an `int?` where the field is genuinely optional; that takes `null` there.

### Dates need a format

A date input requires `yyyy-MM-dd`, and this surface cannot supply that from `type` the way Razor
does. Write it as the argument before the culture:

```csharp
private DateOnly _due = new(2026, 8, 14);

protected override View Body =>
    Input.Type("date").Bind(
        "value", "oninput", () => _due, "yyyy-MM-dd", CultureInfo.InvariantCulture);
```

A format is accepted for `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` and their nullable
forms, and for nothing else — those are the types the framework declares a format-taking converter
for. Writing one for an `int` reports [BCF3031](./diagnostics.md#bcf3031). To format a number, format
it in the getter and parse it in an explicit setter instead.

### If you publish trimmed

Binding any value type roots Blazor's `BindConverter` whole, including converters for types you never
bind. Measured on a trimmed self-contained publish, that is about 10 KB of
`Microsoft.AspNetCore.Components.dll`. An app that binds only `string` and `bool` does not pay it. It
is a one-off, not per binding.

### Doing the conversion yourself

The explicit form still works, and is what you want when the value needs validating rather than
converting:

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

Use `TryParse` rather than `Parse` here. An exception thrown from a setter is not the framework's
rejection path; it faults the render.

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
up on the component type, so a missing or mistyped `{Name}Changed` reports
[BCF3020](./diagnostics.md#bcf3020) rather than binding nothing. This is what makes the asymmetry with
the element side a rule and not an inconsistency.

`{Name}Expression` is the reason the target is written as a getter lambda rather than passed by
reference. A component under an `EditForm` resolves a `FieldIdentifier` from that expression, and the
identifier is what ties the input to a property of your model, so validation messages land on the right
field. No other spelling could supply it.

The `EditForm` around it is written the same way. `EditForm.ChildContent` is a
`RenderFragment<EditContext>`, and the brackets supply it with the context discarded, so the content
below needs nothing else. Content that reads the `EditContext` is named with `.Template` instead:

```csharp
protected override View Body =>
    Component<EditForm>()
        .Param(form => form.Model, _model)[
            Component<NameFields>().Param(fields => fields.Value, _model)];
```

The bound inputs may sit directly in the brackets or in their own component; the binding above, and
the `ValueExpression` it supplies, are unaffected either way. See
[generic fragment parameters](./components-and-reuse.md#generic-fragment-parameters) for the spelling
that reads the `EditContext`, and for when a cached delegate through `.Param` is the better choice.

Under static SSR — no interactive render mode on the page — the property holding `EditForm.Model`
needs `[SupplyParameterFromForm]` on itself, not a plain field. Only a `[SupplyParameterFromForm]`
property is repopulated from the posted form; anything else keeps whatever it held before the POST,
typically `null`. Splitting the bound inputs into their own component does not avoid this either:
`InputText`/`InputTextArea` derive the posted field's name from their own `ValueExpression`, so a
child component's own parameter posts under a different name than the one the holding property
expects back. `samples/Guestbook/Components/Pages/GuestbookPage.cs` shows the working shape.

An explicit setter and an `async` setter are available here too, with the same meaning as on an
element. `TValue` takes no culture and no format: the value goes to a parameter rather than to the
DOM, so nothing formats or parses it on the way and there is no choice to write down.

Remember that `Component<T>()` cannot name a `.razor` component declared in the same project
([BCF3012](./diagnostics.md#bcf3012)). Framework components such as `InputText`, and hand-written C#
components, always resolve.

## What is checked

Six diagnostics read a `.Bind`, and each has an entry in the [reference](./diagnostics.md):

| | |
| --- | --- |
| [BCF3017](./diagnostics.md#bcf3017) | the getter's shape |
| [BCF3018](./diagnostics.md#bcf3018) | a getter-only target that cannot be assigned |
| [BCF3019](./diagnostics.md#bcf3019) | an event name missing its `on` |
| [BCF3020](./diagnostics.md#bcf3020) | a component with no matching change callback |
| [BCF3024](./diagnostics.md#bcf3024) | a bound `class` beside a `.Class` |
| [BCF3031](./diagnostics.md#bcf3031) | a format the value's type has no converter for |

An element may carry more than one `.Bind`. If two of them share an attribute name or an event name,
that is [BCF3010](./diagnostics.md#bcf3010), the same duplicate any two decorations would report. DOM
resynchronization — the repair that puts a normalized value back over what the user typed — applies to
`value` and `checked` only, because those are the only two the browser sends back with the event.

## Next

See [elements and decorations](./elements-and-decorations.md#decorations) for the one-way `.Attr` and
`.On` this is built from, or [components and reuse](./components-and-reuse.md) for `.Param` and the
rest of the component surface.

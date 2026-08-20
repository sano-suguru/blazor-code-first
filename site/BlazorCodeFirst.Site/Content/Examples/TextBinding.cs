using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>two-way-binding.md's figure for an element binding.</summary>
/// <remarks>
/// The output half shows the outward half of the round trip. The event the second argument names
/// leaves no attribute in the markup, which is worth seeing beside the C#: one decoration writes an
/// attribute out and wires a handler back, and only the first of the two is something a reader can
/// look for in the page.
/// </remarks>
internal sealed partial class TextBinding : BodyComponentBase
{
    // Not readonly: the getter-only form derives its setter by assigning to the getter's body, and a
    // readonly field is one of the shapes BCF3018 rejects. The figure is held to a component that
    // compiles, so the rule the document states is enforced on the document's own example.
    private string _name = "Ada";

    // <figure>
    protected override View Body =>
        Div[
            Input.Type("text").Bind("value", "oninput", () => _name),
            P[$"Hello, {_name}"]];
    // </figure>
}

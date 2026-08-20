using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>two-way-binding.md's figure for a checkbox.</summary>
/// <remarks>
/// The reason the element side makes you write the attribute name, shown rather than asserted: the
/// same decoration that produced value= on a text input produces checked= here, and nothing in the
/// C# but that string decides which.
/// </remarks>
internal sealed partial class CheckboxBinding : BodyComponentBase
{
    // Not readonly, for the reason TextBinding records: BCF3018 rejects a readonly field as a
    // getter-only bind target.
    private bool _agreed = true;

    // <figure>
    protected override View Body =>
        Label[
            Input.Type("checkbox").Bind("checked", "onchange", () => _agreed),
            " I agree"];
    // </figure>
}

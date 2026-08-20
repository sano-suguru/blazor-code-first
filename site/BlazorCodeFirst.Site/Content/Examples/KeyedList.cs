using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>control-flow.md's figure for a keyed <c>ForEach</c>.</summary>
/// <remarks>
/// The key is absent from the output half, and that is the figure's point as much as the list is: a
/// key is a diffing instruction rather than markup, so it identifies rows to Blazor and leaves no
/// trace a reader could look for in the page.
/// </remarks>
internal sealed partial class KeyedList : BodyComponentBase
{
    private sealed record Item(int Id, string Name);

    private readonly Item[] _items = [new(1, "Alpha"), new(2, "Beta")];

    // <figure>
    protected override View Body =>
        Ul[
            ForEach(_items,
                key: item => item.Id,
                content: item => Li[item.Name])];
    // </figure>
}

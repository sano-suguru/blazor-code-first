using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>control-flow.md's figure for <c>If</c>.</summary>
/// <remarks>
/// The output half shows one branch, because a render is one state. Which one is decided by the
/// field below rather than by anything the figure hides, so the document can say what it holds and
/// the reader can follow the condition to the markup.
/// </remarks>
internal sealed partial class Conditional : BodyComponentBase
{
    private readonly string[] _items = ["alpha", "beta"];

    // <figure>
    protected override View Body =>
        Div[
            If(_items.Length == 0,
                () => P["Nothing here yet."],
                () => Span[$"{_items.Length} items"])];
    // </figure>
}

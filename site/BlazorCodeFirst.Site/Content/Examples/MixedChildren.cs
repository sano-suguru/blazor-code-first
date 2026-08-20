using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>elements-and-decorations.md's figure for children.</summary>
/// <remarks>
/// A bare string is a text node, which is why the surface has no Text() construct, and an element
/// with no children drops its brackets. The output half shows both without a sentence having to
/// claim them.
/// </remarks>
internal sealed partial class MixedChildren : BodyComponentBase
{
    // <figure>
    protected override View Body =>
        Div[
            "plain text, then ",
            A.Href("/docs")["a link"],
            Br,
            Img.Src("/logo.png").Alt("Logo")];
    // </figure>
}

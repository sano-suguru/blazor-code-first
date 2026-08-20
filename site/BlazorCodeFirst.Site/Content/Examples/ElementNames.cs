using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>elements-and-decorations.md's figure for the element vocabulary.</summary>
/// <remarks>
/// A helper per conforming element, named by its tag with only the first letter uppercased. The
/// output half is what makes that claim checkable: Figcaption is figcaption, and a void element
/// carries its configuration as attributes.
/// </remarks>
internal sealed partial class ElementNames : BodyComponentBase
{
    // <figure>
    protected override View Body =>
        Figure[
            Img.Src("/diagram.png").Alt("Architecture"),
            Figcaption["The compilation pipeline"]];
    // </figure>
}

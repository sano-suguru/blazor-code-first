using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Content.Examples;

/// <summary>elements-and-decorations.md's figure for decorations.</summary>
/// <remarks>
/// Two things a sentence would have to assert separately, shown at once: decorations collapse into
/// the owning element's attributes rather than introducing wrapper nodes, and the class channel
/// joins its values, so two .Class calls leave one attribute.
///
/// No handler. An event registration is not an attribute in the rendered markup, so a figure
/// carrying one would show a C# half with a member the HTML half cannot answer for.
/// </remarks>
internal sealed partial class ChainedDecorations : BodyComponentBase
{
    // <figure>
    protected override View Body =>
        Button
            .Class("btn")
            .Class("btn-primary")
            .Title("Save the current document")["Save"];
    // </figure>
}

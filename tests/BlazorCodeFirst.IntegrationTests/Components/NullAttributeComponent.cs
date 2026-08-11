using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// The three states an attribute value can be in, through real rendering: a value, the empty string, and
/// null. The button flips every spelling at once so a re-render is measured and not only the first
/// pass — null is what the surface promises removes the attribute, and a first render alone cannot show a
/// removal.
/// </summary>
/// <remarks>
/// The last two spans put the null in the class channel's <em>leading</em> term, which is #236's second
/// example and the one the rejected constant-prefix alternative could not have reached: there is no
/// constant ahead of a first term to fold a separator into. One keeps a term behind it and one does not,
/// so the two together separate "the term was dropped" from "the attribute was dropped".
/// </remarks>
public partial class NullAttributeComponent : BodyComponentBase
{
    private bool _present = true;

    protected override View Body =>
        Div[
            Span.Attr("title", _present ? "tip" : null)["to-null"],
            Span.Attr("title", _present ? "tip" : "")["to-empty"],
            Span.Class("card").Class(_present ? "active" : null)["class-join"],
            Span.Class(_present ? "active" : null)["class-single"],
            Span.Class(_present ? "card" : null).Class("active")["class-leading"],
            Span.Class(_present ? "card" : null).Class(_present ? "active" : null)["class-both"],
            Button.OnClick(() => _present = !_present)["toggle"]];
}

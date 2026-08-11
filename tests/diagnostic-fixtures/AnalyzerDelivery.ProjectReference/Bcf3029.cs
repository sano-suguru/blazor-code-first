using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace Fixtures.AnalyzerDelivery;

/// <summary>
/// BCF3029, the second analyzer-reported diagnostic. Like <c>Mutating</c> beside it, this shape compiles:
/// the generator translates the body and emits RenderView, the chain in the event handler is legal C#, and
/// the compilation therefore has no declaration-level error to stop the analyzer driver. Nothing broken may
/// be added here for the reason that file states.
/// </summary>
public partial class Unread : BodyComponentBase
{
    protected override View Body => Button.OnClick(OnClick)["bcf3029"];

    /// <summary>
    /// The motivating shape from #68: design-time syntax in an event handler. It compiles, returns the empty
    /// marker, renders nothing, and never wires <c>OnClick</c> up to the span it appears to decorate.
    /// </summary>
    private void OnClick()
    {
        var unread = Div.Class("card")[Span["bcf3029"]];
    }
}

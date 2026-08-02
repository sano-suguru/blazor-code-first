using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

// Exercises both authoring forms in one Body: children fill ChildContent, .Param fills Footer.
// The counter proves state survives a re-render that changes content inside a slot.
//
// On the bracket surface, children are the last thing written, so the .Param(c => c.Footer, ...) call
// now precedes the indexer that supplies ChildContent. Slots are numbered in source order, so this swaps
// which channel comes first: Footer's sequence number is now lower than ChildContent's, the opposite of
// the pre-flip ordering. The rendered DOM is unaffected — bUnit selects by CSS class, not slot order —
// but the generated RenderView's AddComponentParameter(seq, "Footer", ...) /
// AddComponentParameter(seq, "ChildContent", ...) pair swapped places accordingly.
public partial class SlotHostComponent : BodyComponentBase
{
    private int _count;

    protected override View Body =>
        Div[
            Component<SlotCardComponent>()
                .Param(c => c.Title, "T")
                .Param(c => c.Footer, Span.Class("count")[$"count={_count}"])[
                    Div.Class("marker")["inner"],
                    Button.OnClick(Bump)["bump"]],
            Span.Class("sibling")["after"]];

    private void Bump() => _count++;
}

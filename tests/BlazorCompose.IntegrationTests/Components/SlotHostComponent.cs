using BlazorCompose;
using static BlazorCompose.Html;

namespace BlazorCompose.IntegrationTests.Components;

// Exercises both authoring forms in one Body: children fill ChildContent, .Param fills Footer.
// The counter proves state survives a re-render that changes content inside a slot.
public partial class SlotHostComponent : ComposeComponentBase
{
    private int _count;

    protected override View Body =>
        Div(
            Component<SlotCardComponent>(
                    Div("inner").Class("marker"),
                    Button("bump").OnClick(Bump))
                .Param(c => c.Title, "T")
                .Param(c => c.Footer, Span($"count={_count}").Class("count")),
            Span("after").Class("sibling"));

    private void Bump() => _count++;
}

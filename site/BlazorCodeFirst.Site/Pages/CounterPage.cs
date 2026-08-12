using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>
/// The live demo: events, If, and a keyed ForEach in one component.
/// </summary>
/// <remarks>
/// The buttons are visible and clickable before the WebAssembly runtime starts, and do nothing until
/// it does. That is the accepted consequence of deleting the prerendering wrappers so the
/// prerendered markup reaches a human rather than only a crawler; the reasoning is in the project
/// file, next to BlazorWasmPrerenderingDeleteLoadingContents.
///
/// The figure below the demo is this file, converted by DocGen from site/snippets/manifest. It
/// therefore contains the call that renders it, which is accurate rather than circular: the page
/// really does render its own source.
/// </remarks>
[Route("/counter")]
public sealed partial class CounterPage : BodyComponentBase
{
    // Stable identity keys (not indices) so the generator can diff the list safely.
    private static readonly List<IncrementStep> Steps = [new(1, 1), new(2, 5), new(3, 10)];

    private int _count;

    protected override View Body =>
        Div.Class("shell")[
            Section.Class("demo")[
                Component<PageTitle>()["Counter"],
                H1["Counter"],
                P["A component with state, an event handler, a conditional, and a keyed list."],
                Div.Class("demo-readout")[
                    Span.Class("demo-count")[$"{_count}"],
                    If(_count >= 3, () => Span.Class("demo-milestone")["Milestone reached"])],
                Div.Class("demo-buttons")[
                    Button.Class("chip chip--primary").Attr("type", "button").OnClick(() => _count++)[
                        "Increment"],
                    ForEach(
                        Steps,
                        key: step => step.Id,
                        content: step => Button
                            .Class("chip")
                            .Attr("type", "button")
                            .OnClick(() => _count += step.Amount)[$"+{step.Amount}"])]],
            Section.Class("prose demo-source")[
                H2["This page"],
                P["Everything above is the component below, compiled by BlazorCodeFirst. The "
                    + "figure is generated from the file itself, so it cannot fall behind it."],
                Raw(Snippets.Counter)]];

    private sealed record IncrementStep(int Id, int Amount);
}

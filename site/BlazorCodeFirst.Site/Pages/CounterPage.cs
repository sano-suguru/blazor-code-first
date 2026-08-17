using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using BlazorCodeFirst.Site.Layout;
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

    // The one word this page is called, in the tab, on the card, and as the heading. DocsView passes
    // one expression to all three on a document; this is the same shape for a page with no document.
    private const string Title = "Counter";

    // What a search result shows under that title. A literal, because a demo is not a document: it has
    // no front matter to declare one in and no translation to keep it in step with.
    private const string Summary =
        "A running BlazorCodeFirst component, with the C# that produced it beside the rendered result.";

    private int _count;

    protected override View Body =>
        Div.Class("shell")[
            Section.Class("demo")[
                Component<PageTitle>()[Title],
                Component<SiteMeta>()
                    .Param(m => m.Title, Title)
                    .Param(m => m.Description, Summary)
                    .Param(m => m.Path, "/counter/")
                    .Param(m => m.Lang, Docs.Canonical),
                H1[Title],
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
            Section.Class("demo")[
                Figure.Class("figure")[
                    Figcaption["Pages/CounterPage.cs", Em["this page"]],
                    Div.Class("slab")[Raw(Snippets.Counter)]]]];

    private sealed record IncrementStep(int Id, int Amount);
}

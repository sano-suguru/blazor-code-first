using BlazorCodeFirst;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.IntegrationTests.Components;

/// <summary>
/// A content-taking <c>[Composable]</c> part rendered for real (#34, #176): the caller's bracketed content
/// lands inside the part's markup, an additional <c>View</c> parameter fills a second slot, and both survive
/// a re-render driven by state the content itself reads.
/// </summary>
public partial class ComposableContentComponent : BodyComponentBase
{
    private int _count;

    protected override View Body =>
        Div.Class("page")[
            Panel(H2["Counter"])[
                Span[$"Count: {_count}"],
                Button.OnClick(() => _count++)["Increment"]]];

    [Composable]
    private static ContentView Panel(View header) =>
        Div.Class("panel")[
            Div.Class("panel-head")[header],
            Div.Class("panel-body")[Slot]];
}

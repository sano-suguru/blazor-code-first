using BlazorCodeFirst;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Samples.Guestbook.Components.Pages;

/// <summary>
/// The <c>InteractiveServer</c> island. A static SSR page has no live render channel to filter into,
/// so this owns both its own input and its own results over the open circuit, querying
/// <see cref="GuestbookStore"/> directly rather than filtering the static list rendered by
/// <see cref="GuestbookPage"/>.
/// </summary>
/// <remarks>
/// On the single response to a POST that both dispatches a named-event handler and mutates
/// <see cref="GuestbookStore"/> (<see cref="GuestbookPage.HandleCreate"/>, the per-row delete), this
/// component's prerendered markup reflects the store's state from before that handler ran, while
/// <see cref="GuestbookPage"/>'s own list — not behind a render-mode boundary — reflects the state
/// after. Confirmed as an ASP.NET Core prerendering-pipeline ordering interaction, not a BCF one: a
/// second, ordinary GET immediately afterward renders both in agreement. See the design doc
/// (docs/superpowers/specs/2026-08-20-static-ssr-guestbook-sample-design.md) for the measurement.
/// </remarks>
public sealed partial class LiveSearch : BodyComponentBase
{
    [Inject]
    public required GuestbookStore Store { get; set; }

    private string _query = "";

    protected override View Body => Div.Class("live-search")[
        Input.Type("search").Attr("placeholder", "Filter entries as you type…")
            .Bind("value", "oninput", () => _query, v => _query = v),
        ForEach(Store.Search(_query),
            key: e => e.Id,
            content: entry => P.Class("live-search-result")[$"{entry.Name}: {entry.Message}"])];
}

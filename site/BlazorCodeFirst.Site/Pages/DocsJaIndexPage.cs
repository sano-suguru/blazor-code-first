using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>The Japanese edition's index.</summary>
/// <remarks>
/// "/docs/ja" and "/docs/{Slug}" have the same segment count, so both could match this URL. Blazor
/// prefers the literal segment over the parameter, which is what makes the explicit route win and
/// keeps "/docs/ja" from rendering a not-found body for a document called "ja".
/// </remarks>
[Route("/docs/ja")]
public sealed partial class DocsJaIndexPage : BodyComponentBase
{
    private const string Lang = "ja";

    // An edition with no documents is not a place in the site. Rendering the index anyway would put
    // an empty list under a heading, and the heading would be in English, because a language with no
    // content declares no shell text for one to come from. The publish step lists this route only
    // when the edition has documents, so the two agree.
    protected override View Body =>
        If(Docs.ForLang(Lang).Length > 0,
            () => DocsView.Index(Lang),
            () => NotFoundContent.NotFound());
}

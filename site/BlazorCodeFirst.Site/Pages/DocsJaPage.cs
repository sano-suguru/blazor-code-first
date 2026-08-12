using BlazorCodeFirst;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using static BlazorCodeFirst.Html;

namespace BlazorCodeFirst.Site.Pages;

/// <summary>Renders one document of the Japanese edition.</summary>
/// <remarks>
/// The twin of <see cref="DocsPage"/>, and the whole of what a second language costs on this side:
/// a route template with the edition's prefix, and the language to look the document up by. The body
/// is the same view part.
/// </remarks>
[Route("/docs/ja/{Slug}")]
public sealed partial class DocsJaPage : BodyComponentBase
{
    private const string Lang = "ja";

    /// <inheritdoc cref="DocsPage.Slug"/>
    [Parameter]
    public string? Slug { get; set; }

    private DocEntry? _entry;

    protected override void OnParametersSet() => _entry = Docs.Find(Lang, Slug);

    protected override View Body =>
        If(_entry is not null,
            () => DocsView.Document(_entry!),
            // A slug the canonical edition has but this one has not translated lands here, and gets
            // the same answer as a slug that exists nowhere. The rail is dropped either way: an
            // untranslated document is not a place in this edition of the guide.
            () => NotFoundContent.NotFound());
}

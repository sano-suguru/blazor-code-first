using System.Collections.Immutable;
using BlazorCodeFirst.Site.Content;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace BlazorCodeFirst.Site.Layout;

/// <summary>One head element and the attributes it carries.</summary>
public sealed record HeadTag(string Element, ImmutableArray<KeyValuePair<string, string>> Attributes);

/// <summary>One edition of the page being rendered, and the path it is served from.</summary>
public sealed record DocAlternate(string Lang, string Path);

/// <summary>
/// What a route declares about itself to a search engine and to a social card.
/// </summary>
/// <remarks>
/// Hand-written rather than a BlazorCodeFirst body. The surface mirrors flow content, and a meta or
/// link element is metadata content: Html.Elements declares neither, and adding them for this one
/// component would widen the surface DESIGN.md §4.1 settles. Html.Raw would reach the same markup at
/// the cost of hand-rolling attribute escaping for a description that is prose, while
/// RenderTreeBuilder escapes every attribute value it is given.
///
/// Which tags a route gets is decided by <see cref="Tags"/>, a function of the route's own facts, and
/// BuildRenderTree does nothing but write them out. That split is what makes the decision testable
/// without a DOM, which is the same reason the rest of this project gives (#279).
///
/// Nothing here is emitted from wwwroot/index.html, not even the tags that never vary. HeadOutlet
/// appends to the head and does not replace what is already there, so a static og:site_name would
/// leave the not-found page carrying half a card, and og:image's absolute URL would sit outside
/// <see cref="SiteMetadata"/> where nothing holds it to the origin.
/// </remarks>
public sealed class SiteMeta : ComponentBase
{
    /// <summary>The page's title, as a social card spells it.</summary>
    [Parameter]
    public string Title { get; set; } = default!;

    /// <summary>The sentence a search result shows under the title.</summary>
    [Parameter]
    public string Description { get; set; } = default!;

    /// <summary>This route's path, in the form sitemap.xml declares: "/" or a trailing slash.</summary>
    [Parameter]
    public string Path { get; set; } = default!;

    /// <summary>The language this page is written in.</summary>
    [Parameter]
    public string Lang { get; set; } = default!;

    /// <summary>Every edition of this page, this one included. Empty when the page has only one.</summary>
    [Parameter]
    public IReadOnlyList<DocAlternate> Alternates { get; set; } = [];

    /// <summary>The head elements a route declares about itself.</summary>
    public static ImmutableArray<HeadTag> Tags(
        string title,
        string description,
        string path,
        string lang,
        IReadOnlyList<DocAlternate> alternates)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(lang);
        ArgumentNullException.ThrowIfNull(alternates);

        string url = SiteMetadata.Origin + path;
        var tags = ImmutableArray.CreateBuilder<HeadTag>();

        tags.Add(Meta("name", "description", description));
        tags.Add(new HeadTag("link", [Pair("rel", "canonical"), Pair("href", url)]));

        // Reciprocal: a page that names an alternate has to be named by it in turn, so the set
        // includes the edition being rendered. x-default goes to the canonical edition, which is the
        // answer for a reader whose language this site has no edition for.
        foreach (var alternate in alternates)
        {
            tags.Add(Alternate(alternate.Lang, alternate.Path));
        }

        foreach (var alternate in alternates)
        {
            if (string.Equals(alternate.Lang, Docs.Canonical, StringComparison.Ordinal))
            {
                tags.Add(Alternate("x-default", alternate.Path));
            }
        }

        tags.Add(Meta("property", "og:type", "website"));
        tags.Add(Meta("property", "og:site_name", SiteMetadata.Name));
        tags.Add(Meta("property", "og:title", title));
        tags.Add(Meta("property", "og:description", description));
        tags.Add(Meta("property", "og:url", url));
        tags.Add(Meta("property", "og:locale", Locale(lang)));
        tags.Add(Meta("property", "og:image", SiteMetadata.Origin + SiteMetadata.CardPath));

        tags.Add(Meta("name", "twitter:card", "summary_large_image"));
        tags.Add(Meta("name", "twitter:title", title));
        tags.Add(Meta("name", "twitter:description", description));

        return tags.ToImmutable();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.OpenComponent<HeadContent>(0);
        builder.AddAttribute(1, "ChildContent", (RenderFragment)(content =>
        {
            int sequence = 0;
            foreach (var tag in Tags(Title, Description, Path, Lang, Alternates))
            {
                content.OpenElement(sequence++, tag.Element);
                foreach (var attribute in tag.Attributes)
                {
                    content.AddAttribute(sequence++, attribute.Key, attribute.Value);
                }

                content.CloseElement();
            }
        }));
        builder.CloseComponent();
    }

    /// <summary>The Open Graph spelling of a language tag.</summary>
    /// <remarks>
    /// og:locale is a language AND a territory, which a language tag alone does not carry. The pair is
    /// written out rather than derived, because there is no derivation: which territory stands for an
    /// edition is an editorial choice, not a transformation of its language tag.
    /// </remarks>
    private static string Locale(string lang) => lang switch
    {
        "ja" => "ja_JP",
        _ => "en_US",
    };

    private static HeadTag Alternate(string hreflang, string path) =>
        new(
            "link",
            [
                Pair("rel", "alternate"),
                Pair("hreflang", hreflang),
                Pair("href", SiteMetadata.Origin + path),
            ]);

    private static HeadTag Meta(string key, string name, string content) =>
        new("meta", [Pair(key, name), Pair("content", content)]);

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);
}

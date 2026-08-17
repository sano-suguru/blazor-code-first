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
    /// <remarks>
    /// The trailing slash is the whole of the convention, and it is load-bearing: Workers redirects the
    /// bare form to it, so a canonical link without it would name a URL that answers 307 rather than
    /// the page. This is the one place that reason is written; eng/verify-site-prerender.sh holds every
    /// route to it by comparing against the same form its route table carries.
    /// </remarks>
    [Parameter]
    public string Path { get; set; } = default!;

    /// <summary>The language this page is written in.</summary>
    [Parameter]
    public string Lang { get; set; } = default!;

    /// <summary>Every edition of this page, this one included. Empty when the page has only one.</summary>
    /// <remarks>
    /// A collection parameter, which opts this component out of Blazor's change suppression: only
    /// primitives, string, DateTime, Type and decimal are treated as definitely-equal, so a docs route
    /// rebuilds all fifteen tags on a parent re-render where Home and CounterPage, handed strings only,
    /// rebuild nothing. Measured at 3.3 KB against 136 B.
    ///
    /// Accepted rather than closed by handing this component a slug and letting it compute the
    /// editions itself. A docs page re-renders only when the router re-supplies its parameters, so the
    /// cost today is one hydration render per visit; buying it back would put the documentation
    /// routing rules inside the component that writes the head.
    /// </remarks>
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

        // x-default goes to the canonical edition, which is the answer for a reader whose language
        // this site has no edition for. Picked rather than looped over, so that exactly one is emitted
        // however many entries the caller's list holds. Resolved before the builder, because it
        // decides the builder's size.
        var canonicalEdition = alternates.FirstOrDefault(
            a => string.Equals(a.Lang, Docs.Canonical, StringComparison.Ordinal));

        // Sized rather than grown: without a capacity the builder reallocates 4, 8, 16 and then
        // ToImmutable copies again, which measured 480 of the 2,520 bytes one docs route costs. The
        // twelve is the count of unconditional tags below; get it wrong and this costs one copy, which
        // is what it costs today anyway.
        var tags = ImmutableArray.CreateBuilder<HeadTag>(
            12 + alternates.Count + (canonicalEdition is null ? 0 : 1));

        tags.Add(Meta("name", "description", description));
        tags.Add(new HeadTag("link", [new("rel", "canonical"), new("href", url)]));

        // An hreflang set has to be reciprocal: a page that names an alternate must be named by it in
        // turn, so the set includes the edition being rendered, and a page nothing else points at
        // names none at all. This is the one place that argument is written; the callers say what they
        // pass, and eng/verify-site-prerender.sh counts what arrives.
        foreach (var alternate in alternates)
        {
            tags.Add(Alternate(alternate.Lang, alternate.Path));
        }

        if (canonicalEdition is not null)
        {
            tags.Add(Alternate("x-default", canonicalEdition.Path));
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
                new("rel", "alternate"),
                new("hreflang", hreflang),
                new("href", SiteMetadata.Origin + path),
            ]);

    private static HeadTag Meta(string key, string name, string content) =>
        new("meta", [new(key, name), new("content", content)]);
}

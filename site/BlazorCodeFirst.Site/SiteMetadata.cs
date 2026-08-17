namespace BlazorCodeFirst.Site;

/// <summary>What this deployment calls itself, in the places a page has to say so absolutely.</summary>
/// <remarks>
/// A canonical link and an og:url are absolute by definition. They are read off a copy of the page
/// that may be served from anywhere, and a relative one would resolve against wherever that copy
/// sits. That is also what makes them useful on a preview deployment, whose pages should name
/// production rather than themselves.
///
/// The same origin is in wwwroot/robots.txt, in the ORIGIN of .github/workflows/site.yml, and here.
/// That workflow already holds robots.txt to ORIGIN and holds this file to it beside that check:
/// three copies that can disagree are worth one assertion each.
/// </remarks>
internal static class SiteMetadata
{
    /// <summary>The origin the published site is served from, with no trailing slash.</summary>
    public const string Origin = "https://blazor-code-first-site.snsgr.workers.dev";

    /// <summary>What this site calls itself: the wordmark, the home page's title, the social card.</summary>
    /// <remarks>
    /// It is a C# namespace, which is why css/app.css sets the wordmark in the machine face. The one
    /// place it cannot be read from is tools/og-card.html, which is a stylesheet-and-markup file with
    /// no way to import a constant.
    /// </remarks>
    public const string Name = "BlazorCodeFirst";

    /// <summary>The card image a social preview shows, as a path under <see cref="Origin"/>.</summary>
    public const string CardPath = "/og.png";
}

using Markdig;
using Markdig.Extensions.AutoIdentifiers; // AutoIdentifierOptions enum lives here (UseAutoIdentifiers is in Markdig)
using Markdown.ColorCode;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Converts Markdown to HTML with GitHub-style heading slugs and class-based
/// ColorCode syntax highlighting. Deterministic and side-effect free.</summary>
public static class MarkdownConverter
{
    // Shared, built once. The ColorCode extension's HtmlClassFormatter keeps a mutable
    // TextWriter as instance state, so this pipeline is safe for SEQUENTIAL reuse only
    // (DocGenRunner converts documents in a sequential foreach). Concurrent calls to ToHtml are
    // not supported and can race on that shared formatter state.
    //
    // Front matter is NOT handled here: it is split off as text before conversion (see FrontMatter).
    // Enabling Markdig's UseYamlFrontMatter would make the output depend on extension registration
    // order relative to UseColorCode, which can fail silently.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        // HtmlFormatterType.Css => class-based spans (not inline styles); the shared
        // StyleDictionary makes the emitted classes match HighlightCssEmitter's CSS.
        .UseColorCode(HtmlFormatterType.Css, ColorCodeTheme.Styles)
        .Build();

    /// <summary>Converts a Markdown fragment with no body rules and no link rewriting.</summary>
    public static string ToHtml(string markdown) =>
        Render(markdown, knownSlugs: null, fileName: null, routePrefix: null);

    /// <summary>Converts a document body: enforces the body authoring rules, rewrites
    /// sibling-document links into SPA routes under <paramref name="routePrefix"/>, and adds heading
    /// anchors.</summary>
    public static string ToHtml(
        string markdown,
        IReadOnlySet<string> knownSlugs,
        string fileName,
        string routePrefix)
    {
        ArgumentNullException.ThrowIfNull(knownSlugs);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(routePrefix);
        return Render(markdown, knownSlugs, fileName, routePrefix);
    }

    private static string Render(
        string markdown,
        IReadOnlySet<string>? knownSlugs,
        string? fileName,
        string? routePrefix)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var document = Markdig.Markdown.Parse(markdown, Pipeline);

        if (knownSlugs is not null && fileName is not null && routePrefix is not null)
        {
            MarkdownBodyRules.EnsureNoTopLevelHeading(document, fileName);
            AstRewriter.RewriteRelativeLinks(document, knownSlugs, fileName, routePrefix);
            AstRewriter.AddHeadingLinks(document);
        }

        using var writer = new StringWriter();
        // Use the ToHtml extension rather than constructing an HtmlRenderer: it rents a renderer the
        // pipeline has already Setup(), so the ColorCode renderer registration cannot be missed.
        document.ToHtml(writer, Pipeline);
        return writer.ToString();
    }
}

using Markdig;
using Markdig.Extensions.AutoIdentifiers; // AutoIdentifierOptions enum lives here (UseAutoIdentifiers is in Markdig)
using Markdown.ColorCode;

namespace BlazorCompose.Site.DocGen;

/// <summary>Converts Markdown to HTML with GitHub-style heading slugs and class-based
/// ColorCode syntax highlighting. Deterministic and side-effect free.</summary>
public static class MarkdownConverter
{
    // Shared, built once. The ColorCode extension's HtmlClassFormatter keeps a mutable
    // TextWriter as instance state, so this pipeline is safe for SEQUENTIAL reuse only
    // (DocGenRunner calls ToHtml in a sequential foreach). Concurrent calls to ToHtml are
    // not supported and can race on that shared formatter state.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        // HtmlFormatterType.Css => class-based spans (not inline styles); the shared
        // StyleDictionary makes the emitted classes match HighlightCssEmitter's CSS.
        .UseColorCode(HtmlFormatterType.Css, ColorCodeTheme.Styles)
        .Build();

    public static string ToHtml(string markdown) => Markdig.Markdown.ToHtml(markdown, Pipeline);

    /// <summary>Converts a document body, enforcing the body authoring rules. Used by DocGenRunner;
    /// the single-argument overload stays rule-free for focused unit tests.</summary>
    public static string ToHtml(string markdown, string fileName)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(fileName);

        MarkdownBodyRules.EnsureNoTopLevelHeading(Markdig.Markdown.Parse(markdown, Pipeline), fileName);
        return ToHtml(markdown);
    }
}

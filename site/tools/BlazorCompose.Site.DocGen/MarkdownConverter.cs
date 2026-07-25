using Markdig;
using Markdig.Extensions.AutoIdentifiers; // AutoIdentifierOptions enum lives here (UseAutoIdentifiers is in Markdig)
using Markdown.ColorCode;

namespace BlazorCompose.Site.DocGen;

/// <summary>Converts Markdown to HTML with GitHub-style heading slugs and class-based
/// ColorCode syntax highlighting. Deterministic and side-effect free.</summary>
public static class MarkdownConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        // HtmlFormatterType.Css => class-based spans (not inline styles); the shared
        // StyleDictionary makes the emitted classes match HighlightCssEmitter's CSS.
        .UseColorCode(HtmlFormatterType.Css, ColorCodeTheme.Styles)
        .Build();

    public static string ToHtml(string markdown) => Markdig.Markdown.ToHtml(markdown, Pipeline);
}

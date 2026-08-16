using Markdig;
using Markdig.Extensions.AutoIdentifiers; // AutoIdentifierOptions enum lives here (UseAutoIdentifiers is in Markdig)
using Markdig.Syntax;
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
        // A '|'-delimited table becomes a <table>, which css/app.css styles as its own horizontal
        // scroll container. Registered before UseColorCode because pipe tables are a block-level
        // construct and a fenced block is claimed by the fence parser first, so a '|' inside a code
        // sample stays code; MarkdownConverterTests holds both halves of that.
        .UsePipeTables()
        // HtmlFormatterType.Css => class-based spans (not inline styles); the shared
        // StyleDictionary makes the emitted classes match HighlightCssEmitter's CSS.
        .UseColorCode(HtmlFormatterType.Css, ColorCodeTheme.Styles)
        .Build();

    /// <summary>Converts a Markdown fragment with no body rules and no link rewriting.</summary>
    public static string ToHtml(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Render(Parse(markdown));
    }

    /// <summary>
    /// Parses a document body and applies everything that needs no knowledge of another document:
    /// the CJK line-break join and the body authoring rules.
    /// </summary>
    /// <remarks>
    /// Conversion is split here because a link's fragment names a heading in the document it points
    /// at, and a document's headings are known only once it has been parsed. The caller parses every
    /// document in a language, collects each one's <see cref="HeadingAnchors"/>, and only then calls
    /// <see cref="ToHtml(MarkdownDocument, IReadOnlyDictionary{string, IReadOnlySet{string}}, string,
    /// string)"/> on each — which is the ordering #405 records as missing.
    /// </remarks>
    public static MarkdownDocument ParseBody(string markdown, string fileName)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(fileName);

        var document = Parse(markdown);
        MarkdownBodyRules.EnsureNoTopLevelHeading(document, fileName);
        return document;
    }

    /// <summary>Finishes a parsed document body: rewrites sibling-document links into SPA routes
    /// under <paramref name="routePrefix"/>, adds heading anchors, and renders.</summary>
    /// <remarks>
    /// Call once per parsed document. <see cref="AstRewriter.AddHeadingLinks"/> appends rather than
    /// replaces, so a second call would give every heading a second permalink.
    /// </remarks>
    public static string ToHtml(
        MarkdownDocument document,
        IReadOnlyDictionary<string, IReadOnlySet<string>> anchorsBySlug,
        string fileName,
        string routePrefix)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(anchorsBySlug);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(routePrefix);

        AstRewriter.RewriteRelativeLinks(document, anchorsBySlug, fileName, routePrefix);
        AstRewriter.AddHeadingLinks(document);
        return Render(document);
    }

    private static MarkdownDocument Parse(string markdown)
    {
        var document = Markdig.Markdown.Parse(markdown, Pipeline);

        // Runs for a fragment as well as for a body, unguarded: how a wrapped line reaches the reader
        // is a property of the text, not one of the body rules, and a fragment is read by the same
        // eyes as a document.
        AstRewriter.JoinCjkSoftLineBreaks(document);
        return document;
    }

    private static string Render(MarkdownDocument document)
    {
        using var writer = new StringWriter();
        // Use the ToHtml extension rather than constructing an HtmlRenderer: it rents a renderer the
        // pipeline has already Setup(), so the ColorCode renderer registration cannot be missed.
        document.ToHtml(writer, Pipeline);
        return writer.ToString();
    }
}

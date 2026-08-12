namespace BlazorCodeFirst.Site.DocGen;

/// <summary>
/// Converts one snippet's source text to the same highlighted HTML a document's code fence produces.
/// </summary>
/// <remarks>
/// The conversion is a Markdown fence handed to <see cref="MarkdownConverter"/>'s fragment overload,
/// rather than a second call into ColorCode. Going through the same pipeline is what makes a figure
/// and a prose code block one vocabulary: the classes cannot diverge, because there is only one
/// place that emits them, and <c>HighlightCssEmitterTests</c> already holds that place to the
/// stylesheet.
/// </remarks>
public static class SnippetConverter
{
    /// <summary>The fence language each source extension opens. An extension absent from this map is
    /// an error rather than an unhighlighted fence: a figure that silently lost its highlighting
    /// looks like a theme regression, and the author would have no reason to suspect the
    /// manifest.</summary>
    private static readonly Dictionary<string, string> Languages = new(StringComparer.Ordinal)
    {
        [".cs"] = "csharp",
    };

    public static string ToHtml(string sourceText, string path, string snippetName)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(snippetName);

        string extension = Path.GetExtension(path);
        if (!Languages.TryGetValue(extension, out string? language))
        {
            throw new InvalidOperationException(
                $"Snippet '{snippetName}' reads '{path}', whose extension " +
                $"'{extension}' names no fence language. Mapped extensions are " +
                $"[{string.Join(", ", Languages.Keys)}].");
        }

        // Drop trailing newlines so a file that ends the usual way does not gain a blank last line
        // in the figure. Both characters, because a CRLF file trimmed of its newlines alone would
        // keep the carriage return and the blank line with it.
        //
        // Nothing else is normalized here: Markdig resolves CRLF to LF while parsing, so a second
        // pass would be a line that no input can be found to need.
        string text = sourceText.TrimEnd('\n', '\r');
        string fence = new('`', FenceLength(text));

        return MarkdownConverter.ToHtml($"{fence}{language}\n{text}\n{fence}\n");
    }

    /// <summary>
    /// How many backticks the wrapper needs: one more than the longest run in the source, and never
    /// fewer than three. A C# doc comment may quote a fence, and a wrapper that the source can close
    /// would render the rest of the file as prose.
    /// </summary>
    private static int FenceLength(string text)
    {
        int longest = 0;
        int run = 0;
        foreach (char c in text)
        {
            run = c == '`' ? run + 1 : 0;
            if (run > longest)
            {
                longest = run;
            }
        }

        return Math.Max(3, longest + 1);
    }
}

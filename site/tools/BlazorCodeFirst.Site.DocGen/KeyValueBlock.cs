namespace BlazorCodeFirst.Site.DocGen;

/// <summary>One declared key and its value, in the order the file wrote them.</summary>
public readonly record struct KeyValueLine(string Key, string Value);

/// <summary>
/// Reads a leading <c>---</c> fenced block of <c>key: value</c> lines and hands back what follows it.
/// </summary>
/// <remarks>
/// Three files use this shape: a document's front matter, which is followed by a Markdown body; a
/// language's shell file, which is nothing but the block; and the snippets manifest, likewise.
/// Splitting the scanning out keeps them from drifting into subtly different ideas of what a
/// delimiter or a blank line means, which is the kind of difference an author only discovers through
/// a confusing error message.
///
/// The parser is deliberately strict rather than a general YAML implementation: content is
/// repository-owned, so a malformed line is a build error with a clear message instead of a silently
/// ignored one. Interpreting the keys is the caller's job, because only the caller knows which are
/// required and what their values mean.
/// </remarks>
public static class KeyValueBlock
{
    private const string Fence = "---";

    /// <summary>
    /// Splits <paramref name="raw"/> into its declared lines and the text after the closing fence.
    /// </summary>
    /// <param name="kind">
    /// What the file is, as the error names it: a document, a shell file, a snippet manifest. It is
    /// the caller's to supply for the same reason <paramref name="opening"/> is — this parser is
    /// reached by three kinds of file, and a noun fixed here would be one the next caller has to
    /// agree with.
    /// </param>
    /// <param name="opening">
    /// How to finish the error when the file does not open with a fence. Each caller wants a
    /// different sentence: front matter described to someone editing a document, a shell file that
    /// is entirely block, a manifest declaring one line per snippet.
    /// </param>
    public static (IReadOnlyList<KeyValueLine> Lines, string Remainder) Parse(
        string raw,
        string fileName,
        string kind,
        string opening)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(opening);

        // Normalize CRLF so authoring on Windows behaves identically; the rest keeps LF only.
        string text = raw.Replace("\r\n", "\n", StringComparison.Ordinal);

        int openingNewline = text.IndexOf('\n', StringComparison.Ordinal);
        string openingLine = openingNewline < 0 ? text : text[..openingNewline];
        if (openingNewline < 0 || openingLine != Fence)
        {
            if (openingLine.Trim() == Fence ||
                openingLine.TrimStart().StartsWith(Fence, StringComparison.Ordinal))
            {
                throw Invalid(fileName, kind, "the opening delimiter must be exactly '---' followed by a newline.");
            }

            throw Invalid(fileName, kind, opening);
        }

        int restStart = -1;
        var lines = new List<KeyValueLine>();
        int cursor = openingNewline + 1;
        while (cursor <= text.Length)
        {
            int newline = text.IndexOf('\n', cursor);
            string line = newline < 0 ? text[cursor..] : text[cursor..newline];
            if (line == Fence)
            {
                restStart = newline < 0 ? text.Length : newline + 1;
                break;
            }

            if (line.TrimStart().StartsWith(Fence, StringComparison.Ordinal))
            {
                throw Invalid(fileName, kind, "the closing delimiter must be exactly '---'.");
            }

            if (line.Trim().Length > 0)
            {
                int separator = line.IndexOf(':', StringComparison.Ordinal);
                if (separator < 0)
                {
                    throw Invalid(fileName, kind, $"line '{line}' is not a 'key: value' pair.");
                }

                lines.Add(new KeyValueLine(line[..separator].Trim(), line[(separator + 1)..].Trim()));
            }

            if (newline < 0)
            {
                break;
            }

            cursor = newline + 1;
        }

        if (restStart < 0)
        {
            throw Invalid(fileName, kind, "the block has no closing '---' line.");
        }

        return (lines, text[restStart..]);
    }

    /// <summary>The one place the error is worded. The kind comes from the caller, which is the
    /// only party that knows what the file is.</summary>
    internal static InvalidOperationException Invalid(string fileName, string kind, string reason) =>
        new($"Invalid {kind} '{fileName}': {reason}");
}

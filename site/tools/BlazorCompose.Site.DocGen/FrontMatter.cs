using System.Globalization;

namespace BlazorCompose.Site.DocGen;

/// <summary>The metadata a document declares in its front matter block.</summary>
public sealed record FrontMatterFields(string Title, int Order);

/// <summary>
/// Splits a document's leading <c>---</c> front matter block from its Markdown body and validates
/// the declared fields.
/// </summary>
/// <remarks>
/// The block is separated as plain text BEFORE the Markdown pipeline sees it, deliberately avoiding
/// Markdig's <c>UseYamlFrontMatter</c> extension. That extension registers its renderer with
/// <c>ObjectRenderers.InsertBefore&lt;CodeBlockRenderer&gt;</c>, which returns false instead of
/// throwing when the target is absent — and the ColorCode extension removes the default
/// CodeBlockRenderer during its own setup. Registered after ColorCode, the YAML renderer would
/// silently fail to register and the front matter block (a CodeBlock subclass) would be picked up by
/// ColorCode's renderer, emitting the front matter as a code block at the top of every document with
/// no error at all. Text separation cannot fail that way.
///
/// The parser is deliberately strict rather than a general YAML implementation: content is
/// repository-owned, so an unknown key, a repeated key, or a malformed value is a build error with
/// a clear message instead of a silently ignored line.
/// </remarks>
public static class FrontMatter
{
    private const string Fence = "---";

    public static (FrontMatterFields Fields, string Body) Split(string raw, string fileName)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(fileName);

        // Normalize CRLF so authoring on Windows behaves identically; the body keeps LF only.
        string text = raw.Replace("\r\n", "\n", StringComparison.Ordinal);

        if (!text.StartsWith(Fence + "\n", StringComparison.Ordinal))
        {
            throw Invalid(fileName, "the file must start with a '---' front matter block declaring 'title' and 'order'.");
        }

        int bodyStart = -1;
        var lines = new List<string>();
        int cursor = Fence.Length + 1;
        while (cursor <= text.Length)
        {
            int newline = text.IndexOf('\n', cursor);
            string line = newline < 0 ? text[cursor..] : text[cursor..newline];
            if (line.Trim() == Fence)
            {
                bodyStart = newline < 0 ? text.Length : newline + 1;
                break;
            }

            lines.Add(line);
            if (newline < 0)
            {
                break;
            }

            cursor = newline + 1;
        }

        if (bodyStart < 0)
        {
            throw Invalid(fileName, "the front matter block has no closing '---' line.");
        }

        string? title = null;
        int? order = null;
        foreach (string line in lines)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                throw Invalid(fileName, $"front matter line '{line}' is not a 'key: value' pair.");
            }

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Trim();

            switch (key)
            {
                case "title":
                    if (title is not null)
                    {
                        throw Invalid(fileName, "front matter key 'title' is declared more than once.");
                    }

                    if (value.Length == 0)
                    {
                        throw Invalid(fileName, "front matter 'title' must not be empty.");
                    }

                    title = value;
                    break;

                case "order":
                    if (order is not null)
                    {
                        throw Invalid(fileName, "front matter key 'order' is declared more than once.");
                    }

                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    {
                        throw Invalid(fileName, $"front matter 'order' must be an integer but was '{value}'.");
                    }

                    order = parsed;
                    break;

                default:
                    throw Invalid(fileName, $"front matter key '{key}' is not recognized; only 'title' and 'order' are allowed.");
            }
        }

        if (title is null)
        {
            throw Invalid(fileName, "front matter is missing the required 'title' key.");
        }

        if (order is null)
        {
            throw Invalid(fileName, "front matter is missing the required 'order' key.");
        }

        return (new FrontMatterFields(title, order.Value), text[bodyStart..]);
    }

    private static InvalidOperationException Invalid(string fileName, string reason) =>
        new($"Invalid document '{fileName}': {reason}");
}

using System.Globalization;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>The metadata a document declares in its front matter block.</summary>
/// <param name="SourceHash">
/// A translation's record of the English document it was written against, or null on a canonical
/// document. Whether it is required is the caller's rule, because only the caller knows which
/// language the file came from.
/// </param>
public sealed record FrontMatterFields(string Title, int Order, string? SourceHash);

/// <summary>
/// Splits a document's leading <c>---</c> front matter block from its Markdown body and validates
/// the declared fields.
/// </summary>
/// <remarks>
/// The block is separated as plain text BEFORE the Markdown pipeline sees it, deliberately avoiding
/// Markdig's <c>UseYamlFrontMatter</c> extension. That extension registers its renderer with
/// <c>ObjectRenderers.InsertBefore&lt;CodeBlockRenderer&gt;</c>, which returns false instead of
/// throwing when the target is absent, and the ColorCode extension removes the default
/// CodeBlockRenderer during its own setup. Registered after ColorCode, the YAML renderer would
/// silently fail to register and the front matter block (a CodeBlock subclass) would be picked up by
/// ColorCode's renderer, emitting the front matter as a code block at the top of every document with
/// no error at all. Text separation cannot fail that way.
///
/// The parser is deliberately strict rather than a general YAML implementation: content is
/// repository-owned, so an unknown key, a repeated key, or a malformed value is a build error with
/// a clear message instead of a silently ignored line.
///
/// Scanning the block itself is <see cref="KeyValueBlock"/>'s job, shared with the shell file. Only
/// the reading of these three keys lives here.
/// </remarks>
public static class FrontMatter
{
    public static (FrontMatterFields Fields, string Body) Split(string raw, string fileName)
    {
        var (lines, body) = KeyValueBlock.Parse(
            raw,
            fileName,
            "the file must start with a '---' front matter block declaring 'title' and 'order'.");

        string? title = null;
        int? order = null;
        string? sourceHash = null;
        foreach (var (key, value) in lines)
        {
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

                case "source-hash":
                    if (sourceHash is not null)
                    {
                        throw Invalid(fileName, "front matter key 'source-hash' is declared more than once.");
                    }

                    // Shape is checked here so a typo cannot read as a mismatch. A malformed hash and
                    // a stale one look the same to the comparison, but only one of them is a document
                    // whose translation is behind, and telling the author "this is out of date" when
                    // the real fault is a truncated paste sends them to rewrite the wrong thing.
                    if (!IsSourceHash(value))
                    {
                        throw Invalid(
                            fileName,
                            $"front matter 'source-hash' must be {SourceHashLength} lowercase hex digits but was '{value}'.");
                    }

                    sourceHash = value;
                    break;

                default:
                    throw Invalid(
                        fileName,
                        $"front matter key '{key}' is not recognized; only 'title', 'order' and 'source-hash' are allowed.");
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

        return (new FrontMatterFields(title, order.Value, sourceHash), body);
    }

    /// <summary>How many hex digits a <c>source-hash</c> carries.</summary>
    /// <remarks>
    /// Eight, which is what an author has to retype by hand. The value only ever has to distinguish
    /// one revision of a document from the next revision of the same document, so a collision would
    /// need two versions of one file to share a prefix; the full digest would cost the author 56 more
    /// characters to buy nothing they can use.
    /// </remarks>
    public const int SourceHashLength = 8;

    private static bool IsSourceHash(string value)
    {
        if (value.Length != SourceHashLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static InvalidOperationException Invalid(string fileName, string reason) =>
        KeyValueBlock.Invalid(fileName, reason);
}

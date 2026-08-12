namespace BlazorCodeFirst.Site.DocGen;

/// <summary>The text the documentation shell shows a reader of one language.</summary>
/// <param name="Name">
/// What this language calls itself. It is shown to a reader who is currently reading a different
/// one, so it is written in the language it names rather than translated per edition: someone
/// looking for the Japanese edition of an English page is looking for the Japanese word.
/// </param>
/// <param name="StaleNotice">
/// The sentence a translation carries when it has fallen behind, or null on the canonical language,
/// which has nothing to fall behind.
/// </param>
public sealed record ShellStrings(
    string Name,
    string IndexTitle,
    string IndexLead,
    string RailHeading,
    string LanguageLabel,
    string? StaleNotice,
    string? StaleLink);

/// <summary>
/// Reads one language's <c>shell.yml</c>: the reader-facing text that is not part of any document.
/// </summary>
/// <remarks>
/// These strings live in the content tree rather than in the page components for the same reason the
/// documents do. They are what a reader reads, so they are content, and a translator revising a
/// sentence should not have to edit C# to do it. Keeping them here is also what lets the repository's
/// rule that source is written in English stay literally true: no reader-facing text is in source at
/// all, in either language, so the Japanese edition needs no exception carved into the source rule.
///
/// The canonical language declares its own file too. Reading English out of the same mechanism, and
/// not out of a default baked into this tool, is what makes a missing key in a translation a visible
/// omission rather than a silent fallback to English.
/// </remarks>
public static class ShellFile
{
    /// <summary>The file each language directory declares its shell text in.</summary>
    public const string FileName = "shell.yml";

    public static ShellStrings Parse(string raw, string fileName, bool isCanonical)
    {
        var (lines, remainder) = KeyValueBlock.Parse(
            raw,
            fileName,
            $"the file must be a '---' block declaring the shell text for this language.");

        if (remainder.Trim().Length > 0)
        {
            throw Invalid(
                fileName,
                "there is text after the closing '---'. This file is the block and nothing else; a " +
                "document's body belongs in a document.");
        }

        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in lines)
        {
            if (Array.IndexOf(Keys, key) < 0)
            {
                throw Invalid(
                    fileName,
                    $"key '{key}' is not recognized; only [{string.Join(", ", Keys)}] are allowed.");
            }

            if (!declared.TryAdd(key, value))
            {
                throw Invalid(fileName, $"key '{key}' is declared more than once.");
            }

            if (value.Length == 0)
            {
                throw Invalid(fileName, $"key '{key}' must not be empty.");
            }
        }

        // The stale keys are required of a translation and forbidden on the canonical language, rather
        // than optional on both. Optional would let a translation ship with no way to say it is behind,
        // which is the one message a stale page exists to carry.
        foreach (string key in Keys)
        {
            bool isStaleKey = key is StaleNoticeKey or StaleLinkKey;
            bool required = !isStaleKey || !isCanonical;

            if (required && !declared.ContainsKey(key))
            {
                throw Invalid(fileName, $"the required key '{key}' is missing.");
            }

            if (!required && declared.ContainsKey(key))
            {
                throw Invalid(
                    fileName,
                    $"key '{key}' describes a translation that has fallen behind its canonical " +
                    "document. This is the canonical language and has nothing to fall behind.");
            }
        }

        return new ShellStrings(
            declared["name"],
            declared["index-title"],
            declared["index-lead"],
            declared["rail-heading"],
            declared["language-label"],
            declared.GetValueOrDefault(StaleNoticeKey),
            declared.GetValueOrDefault(StaleLinkKey));
    }

    private const string StaleNoticeKey = "stale-notice";
    private const string StaleLinkKey = "stale-link";

    private static readonly string[] Keys =
        ["name", "index-title", "index-lead", "rail-heading", "language-label", StaleNoticeKey, StaleLinkKey];

    private static InvalidOperationException Invalid(string fileName, string reason) =>
        KeyValueBlock.Invalid(fileName, reason);
}

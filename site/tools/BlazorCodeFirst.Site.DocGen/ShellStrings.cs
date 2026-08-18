using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>The text the documentation shell shows a reader of one language.</summary>
/// <param name="Name">
/// What this language calls itself. It is shown to a reader who is currently reading a different
/// one, so it is written in the language it names rather than translated per edition: someone
/// looking for the Japanese edition of an English page is looking for the Japanese word.
/// </param>
/// <param name="Groups">
/// What this language calls each navigation group, keyed by the group name a document declares. Every
/// group in <see cref="DocGroup.All"/> has an entry, whether or not this language has a document in
/// it: a group with no label would put an English word in a translated rail the first time a document
/// moved into it.
/// </param>
/// <param name="IndexDescription">
/// The sentence a search result shows under this edition's documentation index. It is content in the
/// same sense every other string here is, so it is authored per language in the content tree rather
/// than assembled in source out of the documents the index lists.
/// </param>
/// <param name="StaleNotice">
/// The sentence a translation carries when it has fallen behind, or null on the canonical language,
/// which has nothing to fall behind.
/// </param>
/// <param name="AnchorFilterCount">
/// The sentence the diagnostics filter's live region announces after each keystroke, carrying two
/// named placeholders, <c>{shown}</c> and <c>{total}</c>. Which one comes first is a per-language
/// choice; <see cref="ShellFile.Parse"/> checks that both are present, not their order.
/// </param>
public sealed record ShellStrings(
    string Name,
    string IndexTitle,
    string IndexDescription,
    string IndexLead,
    string RailHeading,
    string LanguageLabel,
    string AnchorFilterLabel,
    string AnchorFilterCount,
    IReadOnlyDictionary<string, string> Groups,
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
public static partial class ShellFile
{
    /// <summary>The file each language directory declares its shell text in.</summary>
    public const string FileName = "shell.yml";

    /// <param name="lang">
    /// Which language's file this is. Two rules read it: the stale keys, which only need to know
    /// whether this is the canonical edition, and <c>index-description</c>, whose length limit is per
    /// language. Taking the tag rather than a bool is what lets the second one stay per language.
    /// </param>
    public static ShellStrings Parse(string raw, string fileName, string lang)
    {
        ArgumentNullException.ThrowIfNull(lang);
        bool isCanonical = lang == DocLang.Canonical;

        var (lines, remainder) = KeyValueBlock.Parse(
            raw,
            fileName,
            Kind,
            "the file must be a '---' block declaring the shell text for this language.");

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

            ValidatePlaceholders(key, value, fileName);
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
            // Bounded the same way a document's own description is. The index is one page a search
            // result can show, and a limit that stopped at the documents would leave the one page
            // whose text is not a document's unbounded for no stated reason.
            DocDescription.Validate(
                declared["index-description"], lang, fileName, "index-description", Kind),
            declared["index-lead"],
            declared["rail-heading"],
            declared["language-label"],
            declared["anchor-filter-label"],
            declared["anchor-filter-count"],
            DocGroup.All.ToDictionary(g => g, g => declared[GroupKey(g)], StringComparer.Ordinal),
            declared.GetValueOrDefault(StaleNoticeKey),
            declared.GetValueOrDefault(StaleLinkKey));
    }

    /// <summary>The shell key carrying one navigation group's heading.</summary>
    public static string GroupKey(string group) => "group-" + group;

    private const string StaleNoticeKey = "stale-notice";
    private const string StaleLinkKey = "stale-link";

    private static readonly string[] Keys =
    [
        "name", "index-title", "index-description", "index-lead", "rail-heading", "language-label",
        "anchor-filter-label", "anchor-filter-count",
        .. DocGroup.All.Select(GroupKey),
        StaleNoticeKey, StaleLinkKey,
    ];

    /// <summary>The named placeholders a key's value must carry, keyed by that key. A key absent from
    /// here must carry none: <see cref="ValidatePlaceholders"/> is what a <c>{word}</c> placeholder
    /// pasted into an ordinary sentence fails against, rather than reaching the reader as literal
    /// braces. An unpaired brace with no matching close is not a placeholder shape this matches, so
    /// it passes through unchecked, same as any other punctuation.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> PlaceholdersByKey =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // The count a screen reader hears after the diagnostics filter narrows. Which placeholder
            // comes first is a per-language choice (a translation may put the total before the shown
            // count), so this is a set to satisfy, not an order.
            ["anchor-filter-count"] = ["shown", "total"],
        };

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderPattern();

    private static void ValidatePlaceholders(string key, string value, string fileName)
    {
        string[] expected = PlaceholdersByKey.GetValueOrDefault(key, []);
        var found = new HashSet<string>(
            PlaceholderPattern().Matches(value).Select(m => m.Groups[1].Value), StringComparer.Ordinal);

        if (!found.SetEquals(expected))
        {
            string wanted = expected.Length == 0
                ? "no placeholders"
                : "exactly the placeholders " + string.Join(" and ", expected.Select(p => "{" + p + "}"));
            throw Invalid(fileName, $"key '{key}' must use {wanted}, but the value is '{value}'.");
        }
    }

    /// <summary>What the error calls a file this parser reads.</summary>
    private const string Kind = "shell file";

    private static InvalidOperationException Invalid(string fileName, string reason) =>
        KeyValueBlock.Invalid(fileName, Kind, reason);
}

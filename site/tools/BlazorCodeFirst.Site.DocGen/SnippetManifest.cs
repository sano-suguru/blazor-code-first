using System.Text;
using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>One declared snippet: the name it is known by, the member it becomes, and where its
/// text is read from.</summary>
/// <param name="Path">Relative to the snippets directory. It may leave that directory, which is how
/// a figure reaches a file that is also compiled.</param>
public sealed record SnippetEntry(string Name, string MemberName, string Path);

/// <summary>
/// Reads the snippets directory's <c>manifest</c>: which files become figures, and under what names.
/// </summary>
/// <remarks>
/// A manifest rather than a directory scan, because a snippet's source may be a file that is also
/// compiled and therefore lives outside the snippets directory. Declaring every source one way keeps
/// the two kinds from needing two rules, and puts a path that leaves the directory on the line that
/// declares it.
/// <para>
/// The name rule is stricter than "a valid identifier once mapped" for a reason beyond taste. Each
/// part must open with a lowercase letter, so every part contributes exactly one uppercase letter to
/// the mapped name and the mapping is injective: two distinct names cannot reach the same member.
/// Allowing a part to open with a digit would lose that (<c>x-1a</c> and <c>x1a</c> both reach
/// <c>X1a</c>) and would also let a part produce an invalid identifier.
/// </para>
/// </remarks>
public static class SnippetManifest
{
    /// <summary>The file the snippets directory declares its snippets in.</summary>
    public const string FileName = "manifest";

    private static readonly Regex NamePattern =
        new("^[a-z][a-z0-9]*(-[a-z][a-z0-9]*)*$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<SnippetEntry> Parse(string raw, string fileName)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(fileName);

        var (lines, remainder) = KeyValueBlock.Parse(
            raw,
            fileName,
            "the file must be a '---' block declaring 'name: path' for each snippet.");

        if (remainder.Trim().Length > 0)
        {
            throw KeyValueBlock.Invalid(
                fileName,
                "there is text after the closing '---'. This file is the block and nothing else.");
        }

        var entries = new List<SnippetEntry>(lines.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, path) in lines)
        {
            if (!NamePattern.IsMatch(name))
            {
                throw KeyValueBlock.Invalid(
                    fileName,
                    $"snippet name '{name}' is not kebab-case. Each part must open with a lowercase " +
                    "letter and may continue with lowercase letters and digits, joined by single " +
                    "dashes, so the name maps to exactly one member name.");
            }

            if (!seen.Add(name))
            {
                throw KeyValueBlock.Invalid(fileName, $"snippet '{name}' is declared twice.");
            }

            if (path.Length == 0)
            {
                throw KeyValueBlock.Invalid(fileName, $"snippet '{name}' declares no path.");
            }

            entries.Add(new SnippetEntry(name, MemberName(name), path));
        }

        return entries;
    }

    /// <summary>The member one name becomes: each dash-separated part with its first letter
    /// uppercased, concatenated.</summary>
    private static string MemberName(string name)
    {
        var member = new StringBuilder(name.Length);
        foreach (string part in name.Split('-'))
        {
            member.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        }

        return member.ToString();
    }
}

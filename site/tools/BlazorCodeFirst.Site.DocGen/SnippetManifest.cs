using System.Text;
using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>One declared snippet: the name it is known by, and where its text is read from.</summary>
/// <param name="Name">
/// Kebab-case, as the manifest declares it. <see cref="SnippetManifest"/> is what enforces that
/// shape, so an entry reaching an emitter has already passed it.
/// </param>
/// <param name="Path">Relative to the snippets directory. It may leave that directory, which is how
/// a figure reaches a file that is also compiled.</param>
public sealed record SnippetEntry(string Name, string Path)
{
    /// <summary>
    /// The member this snippet becomes: each dash-separated part with its first letter uppercased,
    /// concatenated.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, because it carries no information <see cref="Name"/> does not.
    /// Held as a field it would be a second spelling that has to agree, and a caller building an
    /// entry by hand -- a test, say -- could pair a name with a member no manifest could produce.
    /// </remarks>
    public string MemberName
    {
        get
        {
            var member = new StringBuilder(Name.Length);
            foreach (string part in Name.Split('-'))
            {
                member.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
            }

            return member.ToString();
        }
    }
}

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

    /// <summary>What the error calls a file this parser reads.</summary>
    private const string Kind = "snippet manifest";

    private static readonly Regex NamePattern =
        new("^[a-z][a-z0-9]*(-[a-z][a-z0-9]*)*$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<SnippetEntry> Parse(string raw, string fileName)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(fileName);

        var (lines, remainder) = KeyValueBlock.Parse(
            raw,
            fileName,
            Kind,
            "the file must be a '---' block declaring 'name: path' for each snippet.");

        if (remainder.Trim().Length > 0)
        {
            throw Invalid(
                fileName,
                "there is text after the closing '---'. This file is the block and nothing else.");
        }

        var entries = new List<SnippetEntry>(lines.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, path) in lines)
        {
            if (!NamePattern.IsMatch(name))
            {
                throw Invalid(
                    fileName,
                    $"snippet name '{name}' is not kebab-case. Each part must open with a lowercase " +
                    "letter and may continue with lowercase letters and digits, joined by single " +
                    "dashes, so the name maps to exactly one member name.");
            }

            if (!seen.Add(name))
            {
                throw Invalid(fileName, $"snippet '{name}' is declared twice.");
            }

            if (path.Length == 0)
            {
                throw Invalid(fileName, $"snippet '{name}' declares no path.");
            }

            entries.Add(new SnippetEntry(name, path));
        }

        return entries;
    }

    private static InvalidOperationException Invalid(string fileName, string reason) =>
        KeyValueBlock.Invalid(fileName, Kind, reason);
}

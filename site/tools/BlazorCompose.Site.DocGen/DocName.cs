using System.Globalization;
using System.Text;

namespace BlazorCompose.Site.DocGen;

/// <summary>Maps a Markdown file name stem to a PascalCase C# identifier.</summary>
public static class DocName
{
    /// <summary>Converts e.g. "getting-started" to "GettingStarted". Non-alphanumeric
    /// runs are treated as word boundaries. Throws when the stem yields no identifier
    /// characters; prefixes "_" when the result would start with a digit.</summary>
    public static string ToIdentifier(string fileNameStem)
    {
        ArgumentNullException.ThrowIfNull(fileNameStem);

        var sb = new StringBuilder(fileNameStem.Length);
        bool startWord = true;
        foreach (char c in fileNameStem)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(startWord ? char.ToUpper(c, CultureInfo.InvariantCulture) : c);
                startWord = false;
            }
            else
            {
                startWord = true;
            }
        }

        if (sb.Length == 0)
        {
            throw new ArgumentException(
                $"File name stem '{fileNameStem}' contains no alphanumeric characters.",
                nameof(fileNameStem));
        }

        if (char.IsDigit(sb[0]))
        {
            sb.Insert(0, '_');
        }

        return sb.ToString();
    }
}

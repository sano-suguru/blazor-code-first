using System.Globalization;
using System.Text;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Escapes a string into the body of a C# double-quoted literal.</summary>
/// <remarks>
/// Shared by the two emitters rather than owned by either. Both write generated C# holding
/// repository text, and a difference between their escaping would be a difference in which inputs
/// produce a file that does not compile.
/// </remarks>
internal static class CSharpLiteral
{
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    // U+0085 (NEL), U+2028 (LINE SEPARATOR), and U+2029 (PARAGRAPH SEPARATOR)
                    // are >= 0x20 but are classified as new-line-characters by the C# lexer,
                    // so they are forbidden inside a non-verbatim string literal (CS1010) and
                    // must be escaped just like the control characters below 0x20.
                    if (c is < ' ' or '\u0085' or '\u2028' or '\u2029')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        return sb.ToString();
    }
}

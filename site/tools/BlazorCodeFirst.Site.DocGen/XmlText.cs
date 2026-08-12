using System.Text;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Escapes a string into XML character data.</summary>
/// <remarks>
/// Sits beside <see cref="CSharpLiteral"/> for the same reason it exists: the emitters write
/// generated C# holding repository text, and that text reaches two grammars, not one. A value goes
/// into a string literal and a path goes into a doc comment, so escaping only the first leaves the
/// second able to emit a file that does not compile.
/// <para>
/// Both delimiters are escaped, and the ampersand first so an already-escaped entity is not
/// double-escaped by the later replacements. Quotes are left alone: this escapes character data,
/// not an attribute value, and neither emitter puts repository text in an attribute.
/// </para>
/// </remarks>
internal static class XmlText
{
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        foreach (char c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(c); break;
            }
        }

        return sb.ToString();
    }
}

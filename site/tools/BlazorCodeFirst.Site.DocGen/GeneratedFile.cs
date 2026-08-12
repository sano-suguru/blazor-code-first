using System.Text;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Writes a generated artifact deterministically: UTF-8 without a BOM, and the
/// directory created if it is missing.</summary>
/// <remarks>
/// Newlines are not normalized here. Each emitter writes LF already, and normalizing at the write
/// would hide an emitter that had stopped doing so.
/// </remarks>
internal static class GeneratedFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content, Utf8NoBom);
    }
}

using System.Text;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Writes a generated artifact deterministically: UTF-8 without a BOM, the directory
/// created if it is missing, and an unchanged file left untouched.</summary>
/// <remarks>
/// Newlines are not normalized here. Each emitter writes LF already, and normalizing at the write
/// would hide an emitter that had stopped doing so.
///
/// The bytes are compared before the write because the site build runs this tool before it compiles,
/// and MSBuild decides what is out of date from write times. An unconditional write would move every
/// artifact's timestamp on every build, so the site app would recompile and each target downstream of
/// it would re-run over output identical to what was already there. Comparison is what makes "the
/// build regenerates" cost nothing when there is nothing to regenerate.
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

        byte[] bytes = Utf8NoBom.GetBytes(content);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            return;
        }

        File.WriteAllBytes(path, bytes);
    }
}

using System.Text;

namespace BlazorCompose.Site.DocGen;

/// <summary>Orchestrates the build-time conversion in two passes and writes the two committed
/// artifacts (Docs.g.cs and highlight.css) deterministically (UTF-8 no BOM, LF).</summary>
/// <remarks>
/// Two passes are required because cross-document validation must happen before any conversion:
/// pass 1 reads and validates every document, and <see cref="Run"/> derives the complete slug set
/// from that output before pass 2 begins; pass 2 then converts each document, rewriting relative
/// links and failing the build on a link that points at a document that does not exist.
/// </remarks>
public static class DocGenRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private sealed record DocSource(DocMeta Meta, string FileName, string Body);

    public static void Run(string contentDir, string docsOutPath, string cssOutPath)
    {
        var sources = ReadAndValidate(contentDir);

        // Pass 2 needs the complete slug set so a link to a missing document fails the build.
        var knownSlugs = sources.Select(s => s.Meta.Slug).ToHashSet(StringComparer.Ordinal);

        var docs = new List<(DocMeta Meta, string Html)>(sources.Count);
        foreach (var source in sources)
        {
            docs.Add((source.Meta, MarkdownConverter.ToHtml(source.Body, knownSlugs, source.FileName)));
        }

        // Artifacts are LF-normalized by their emitters; write bytes as-is.
        WriteFile(docsOutPath, CSharpDocEmitter.Emit(docs));
        WriteFile(cssOutPath, HighlightCssEmitter.Emit());
    }

    /// <summary>Pass 1: read every document and validate its file name and front matter. Ordered by
    /// file name (Ordinal) so failures are reported deterministically.</summary>
    private static List<DocSource> ReadAndValidate(string contentDir)
    {
        var files = Directory.EnumerateFiles(contentDir, "*.md")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        var sources = new List<DocSource>(files.Count);
        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            string slug = DocSlug.Validate(Path.GetFileNameWithoutExtension(file), fileName);
            var (fields, body) = FrontMatter.Split(File.ReadAllText(file), fileName);
            sources.Add(new DocSource(new DocMeta(slug, fields.Title, fields.Order), fileName, body));
        }

        return sources;
    }

    private static void WriteFile(string path, string content)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content, Utf8NoBom);
    }
}

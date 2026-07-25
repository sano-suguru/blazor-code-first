using System.Text;

namespace BlazorCompose.Site.DocGen;

/// <summary>Orchestrates the build-time conversion: enumerate content/*.md (Ordinal
/// order), convert each to HTML, and write the two committed artifacts (Docs.g.cs and
/// highlight.css) deterministically (UTF-8 no BOM, LF).</summary>
public static class DocGenRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void Run(string contentDir, string docsOutPath, string cssOutPath)
    {
        var mdFiles = Directory.EnumerateFiles(contentDir, "*.md")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        var docs = new List<(string Identifier, string Html)>(mdFiles.Count);
        foreach (string file in mdFiles)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            string identifier = DocName.ToIdentifier(stem);
            string markdown = File.ReadAllText(file);
            string html = MarkdownConverter.ToHtml(markdown);
            docs.Add((identifier, html));
        }

        string docsSource = CSharpDocEmitter.Emit(docs);
        string css = HighlightCssEmitter.Emit();

        // Artifacts are LF-normalized by their emitters; write bytes as-is.
        File.WriteAllText(docsOutPath, docsSource, Utf8NoBom);
        File.WriteAllText(cssOutPath, css, Utf8NoBom);
    }
}

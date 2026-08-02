using System.Text;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Orchestrates the build-time conversion in two passes and writes the two committed
/// artifacts (Docs.g.cs and highlight.css) deterministically (UTF-8 no BOM, LF).</summary>
/// <remarks>
/// Two passes are required because cross-document validation must happen before any conversion:
/// pass 1 reads and validates every document — file name, front matter, and duplicate order — and
/// <see cref="Run"/> derives the complete slug set from that output before pass 2 begins; pass 2 then
/// converts each document, rewriting relative links and failing the build on a link that points at a
/// document that does not exist.
///
/// Duplicate order is detected here rather than in <see cref="CSharpDocEmitter"/> because only pass 1
/// holds the file name of each colliding document, so only pass 1 can name both halves of the
/// collision. The emitter keeps its own duplicate checks as a defensive assertion at its public
/// boundary.
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

    /// <summary>Pass 1: read every document, validate its file name and front matter, and reject a
    /// duplicate front matter order. Ordered by file name (Ordinal) so failures are reported
    /// deterministically and the "already declared by" half of a collision message is stable.</summary>
    private static List<DocSource> ReadAndValidate(string contentDir)
    {
        var files = Directory.EnumerateFiles(contentDir, "*.md")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        // Maps an order to the first file that declared it, so a collision can name both halves.
        // Duplicate slugs are not checked here: file names are unique within one directory and
        // DocSlug forbids uppercase, so two documents cannot reach this loop with the same slug.
        // CSharpDocEmitter keeps that check as a defensive assertion at its own public boundary.
        var orderOwners = new Dictionary<int, string>();

        var sources = new List<DocSource>(files.Count);
        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            string slug = DocSlug.Validate(Path.GetFileNameWithoutExtension(file), fileName);
            var (fields, body) = FrontMatter.Split(File.ReadAllText(file), fileName);

            if (orderOwners.TryGetValue(fields.Order, out string? orderOwner))
            {
                throw Invalid(
                    fileName,
                    $"front matter order {fields.Order} is already declared by '{orderOwner}'. " +
                    "Each document must declare a distinct 'order' so navigation ordering is unambiguous.");
            }

            orderOwners.Add(fields.Order, fileName);
            sources.Add(new DocSource(new DocMeta(slug, fields.Title, fields.Order), fileName, body));
        }

        return sources;
    }

    private static InvalidOperationException Invalid(string fileName, string reason) =>
        new($"Invalid document '{fileName}': {reason}");

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

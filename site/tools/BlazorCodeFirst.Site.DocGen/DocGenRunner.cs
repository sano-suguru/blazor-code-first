using System.Security.Cryptography;
using System.Text;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Orchestrates the build-time conversion in two passes and writes the two committed
/// artifacts (Docs.g.cs and highlight.css) deterministically (UTF-8 no BOM, LF).</summary>
/// <remarks>
/// Two passes are required because cross-document validation must happen before any conversion:
/// pass 1 reads and validates every document, file name, front matter, and duplicate order, and
/// <see cref="Run"/> derives the per-language slug sets from that output before pass 2 begins; pass 2
/// then converts each document, rewriting relative links and failing the build on a link that points
/// at a document that does not exist in the same language.
///
/// Duplicate order is detected here rather than in <see cref="CSharpDocEmitter"/> because only pass 1
/// holds the file name of each colliding document, so only pass 1 can name both halves of the
/// collision. The emitter keeps its own duplicate checks as a defensive assertion at its public
/// boundary.
/// </remarks>
public static class DocGenRunner
{
    private sealed record DocSource(DocMeta Meta, string FileName, string Body, string? SourceHash);

    public static void Run(string contentDir, string docsOutPath, string cssOutPath) =>
        Run(contentDir, docsOutPath, cssOutPath, Console.Out);

    /// <summary>The same run with its stale-translation report written to <paramref name="report"/>.</summary>
    public static void Run(string contentDir, string docsOutPath, string cssOutPath, TextWriter report)
    {
        ArgumentNullException.ThrowIfNull(report);

        RejectUnknownDirectories(contentDir);

        var sources = new List<DocSource>();
        var shells = new Dictionary<string, ShellStrings>(StringComparer.Ordinal);
        foreach (string lang in DocLang.All)
        {
            var forLang = ReadAndValidate(contentDir, lang);
            if (forLang.Count == 0)
            {
                // A language nobody has translated into needs no shell text: nothing routes to it, so
                // requiring the file would be an obligation with no page behind it.
                continue;
            }

            sources.AddRange(forLang);
            shells.Add(lang, ReadShell(contentDir, lang));
        }

        sources = ResolveStaleness(sources, report);

        // Pass 2 needs each language's own slug set, so a link to a document that has no counterpart
        // in the linking document's language fails the build rather than resolving to a route that
        // was never prerendered.
        var slugsByLang = sources
            .GroupBy(s => s.Meta.Lang, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<string>)g.Select(s => s.Meta.Slug).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        var docs = new List<(DocMeta Meta, string Html)>(sources.Count);
        foreach (var source in sources)
        {
            docs.Add((
                source.Meta,
                MarkdownConverter.ToHtml(
                    source.Body,
                    slugsByLang[source.Meta.Lang],
                    source.FileName,
                    DocLang.RoutePrefix(source.Meta.Lang))));
        }

        // Artifacts are LF-normalized by their emitters; write bytes as-is.
        GeneratedFile.Write(docsOutPath, CSharpDocEmitter.Emit(docs, shells));
        GeneratedFile.Write(cssOutPath, HighlightCssEmitter.Emit());
    }

    /// <summary>
    /// Fails on any subdirectory of the content root that is not a language.
    /// </summary>
    /// <remarks>
    /// Before languages existed, a file in a subdirectory was simply not enumerated, so a document
    /// filed one level too deep produced no route, no error, and no output to notice. Now that a
    /// directory names a language, an unrecognized one is the only reading left, and it is an error
    /// rather than a silent drop.
    /// </remarks>
    private static void RejectUnknownDirectories(string contentDir)
    {
        foreach (string dir in Directory.EnumerateDirectories(contentDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(dir);
            if (Array.IndexOf(DocLang.All, name) < 0)
            {
                throw new InvalidOperationException(
                    $"Invalid content directory '{name}': a subdirectory of the content root names the " +
                    $"language of the documents in it, and '{name}' is not one of " +
                    $"[{string.Join(", ", DocLang.All.Where(l => l != DocLang.Canonical))}]. " +
                    "The canonical language's documents are the top-level files.");
            }
        }
    }

    /// <summary>Reads one language's shell text, which is required of any language that has documents.</summary>
    private static ShellStrings ReadShell(string contentDir, string lang)
    {
        string? subdirectory = DocLang.Directory(lang);
        string dir = subdirectory is null ? contentDir : Path.Combine(contentDir, subdirectory);
        string path = Path.Combine(dir, ShellFile.FileName);
        string fileName = subdirectory is null ? ShellFile.FileName : $"{subdirectory}/{ShellFile.FileName}";

        if (!File.Exists(path))
        {
            throw Invalid(
                fileName,
                $"language '{lang}' has documents but declares no shell text. Every string a reader " +
                "sees lives in the content tree, including the ones that are not part of a document.");
        }

        return ShellFile.Parse(File.ReadAllText(path), fileName, lang == DocLang.Canonical);
    }

    /// <summary>Pass 1 for one language: read every document, validate its file name and front matter,
    /// and reject a duplicate front matter order within that language. Ordered by file name (Ordinal)
    /// so failures are reported deterministically and the "already declared by" half of a collision
    /// message is stable.</summary>
    private static List<DocSource> ReadAndValidate(string contentDir, string lang)
    {
        string? subdirectory = DocLang.Directory(lang);
        string dir = subdirectory is null ? contentDir : Path.Combine(contentDir, subdirectory);
        if (!Directory.Exists(dir))
        {
            // A language nobody has translated into yet is not an error. The canonical directory is
            // the content root, which the caller has already handed us.
            return [];
        }

        var files = Directory.EnumerateFiles(dir, "*.md")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        // Maps an order to the first file that declared it, so a collision can name both halves.
        // Order is unique per language rather than across the tree: a translation sits at the same
        // position in its own navigation as the document it translates, so the two share a number by
        // design. Duplicate slugs are not checked here: file names are unique within one directory
        // and DocSlug forbids uppercase, so two documents cannot reach this loop with the same slug.
        // CSharpDocEmitter keeps that check as a defensive assertion at its own public boundary.
        var orderOwners = new Dictionary<int, string>();

        var sources = new List<DocSource>(files.Count);
        foreach (string file in files)
        {
            string fileName = subdirectory is null
                ? Path.GetFileName(file)
                : $"{subdirectory}/{Path.GetFileName(file)}";
            string slug = DocSlug.Validate(Path.GetFileNameWithoutExtension(file), fileName);
            var (fields, body) = FrontMatter.Split(File.ReadAllText(file), fileName);

            if (orderOwners.TryGetValue(fields.Order, out string? orderOwner))
            {
                throw Invalid(
                    fileName,
                    $"front matter order {fields.Order} is already declared by '{orderOwner}'. " +
                    "Each document must declare a distinct 'order' within its language so navigation " +
                    "ordering is unambiguous.");
            }

            if (lang == DocLang.Canonical && fields.SourceHash is not null)
            {
                throw Invalid(
                    fileName,
                    "front matter declares 'source-hash', which records the document a translation was " +
                    "written against. This is the canonical language and has nothing to track.");
            }

            if (lang != DocLang.Canonical && fields.SourceHash is null)
            {
                throw Invalid(
                    fileName,
                    "front matter is missing the required 'source-hash' key. A translation records the " +
                    "canonical document it was written against, and an absent hash would read as up to " +
                    "date rather than as unchecked.");
            }

            orderOwners.Add(fields.Order, fileName);
            sources.Add(new DocSource(
                new DocMeta(slug, fields.Title, fields.Order, lang, Stale: false),
                fileName,
                body,
                fields.SourceHash));
        }

        return sources;
    }

    /// <summary>
    /// Pairs every translation with its canonical document, marks the ones whose recorded hash no
    /// longer matches, and reports them.
    /// </summary>
    /// <remarks>
    /// A mismatch does not fail the build. The whole point of a canonical language is that it may
    /// move first, and failing here would turn every one-line English edit into an obligation to
    /// rewrite the translation in the same commit. What must not happen is the drift going unsaid, so
    /// the page carries a notice and this writes the list plus each expected hash, which is what the
    /// author pastes back once the translation has actually been revised.
    /// </remarks>
    private static List<DocSource> ResolveStaleness(List<DocSource> sources, TextWriter report)
    {
        var canonical = sources
            .Where(s => s.Meta.Lang == DocLang.Canonical)
            .ToDictionary(s => s.Meta.Slug, StringComparer.Ordinal);

        var resolved = new List<DocSource>(sources.Count);
        var stale = new List<(string FileName, string Expected)>();

        foreach (var source in sources)
        {
            if (source.Meta.Lang == DocLang.Canonical)
            {
                resolved.Add(source);
                continue;
            }

            if (!canonical.TryGetValue(source.Meta.Slug, out var origin))
            {
                throw Invalid(
                    source.FileName,
                    $"there is no canonical document '{source.Meta.Slug}.md' for this translation to " +
                    "follow. The canonical language leads, so a translation of a document that does not " +
                    "exist has nothing to be a translation of.");
            }

            string expected = SourceHashOf(origin);
            bool isStale = !string.Equals(source.SourceHash, expected, StringComparison.Ordinal);
            if (isStale)
            {
                stale.Add((source.FileName, expected));
            }

            resolved.Add(source with { Meta = source.Meta with { Stale = isStale } });
        }

        foreach (var (fileName, expected) in stale.OrderBy(s => s.FileName, StringComparer.Ordinal))
        {
            report.WriteLine(
                $"stale translation: {fileName} was written against an older revision; " +
                $"revise it and set 'source-hash: {expected}'.");
        }

        return resolved;
    }

    /// <summary>
    /// The canonical document's identity as a translation records it: the first
    /// <see cref="FrontMatter.SourceHashLength"/> hex digits of SHA-256 over its title and body.
    /// </summary>
    /// <remarks>
    /// Title and body, and nothing else. The body is already LF-normalized by
    /// <see cref="FrontMatter.Split"/>, so the hash does not move when a file is edited on Windows.
    /// Front matter <c>order</c> is left out on purpose: renumbering the navigation does not change a
    /// word a reader sees, and folding it in would mark every translation stale over a change no
    /// translator has anything to do about.
    /// </remarks>
    private static string SourceHashOf(DocSource canonical)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.Meta.Title + "\n" + canonical.Body));
        return Convert.ToHexStringLower(digest)[..FrontMatter.SourceHashLength];
    }

    private static InvalidOperationException Invalid(string fileName, string reason) =>
        new($"Invalid document '{fileName}': {reason}");
}

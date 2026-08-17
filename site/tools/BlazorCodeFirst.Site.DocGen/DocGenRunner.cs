using System.Security.Cryptography;
using System.Text;

namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Orchestrates the build-time conversion in two passes and writes the two committed
/// artifacts (Docs.g.cs and highlight.css) deterministically (UTF-8 no BOM, LF).</summary>
/// <remarks>
/// Two passes are required because cross-document validation must happen before any conversion:
/// pass 1 reads and validates every document, file name, front matter, and duplicate order; pass 2
/// parses all of them, derives each language's slugs and heading anchors from the parsed set, and
/// only then converts each document, rewriting relative links and failing the build on a link that
/// points at a document that does not exist in the same language or at a heading that document does
/// not publish.
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

        // Pass 2 parses every document before it converts any of them. A link resolves to a document
        // and to a heading inside it, and a document's headings are known only once it has been
        // parsed, so nothing can be checked until the whole set is in hand.
        var parsed = sources
            .Select(s => (Source: s, Document: MarkdownConverter.ParseBody(s.Body, s.FileName)))
            .ToList();

        // Canonical only. SentenceLength says why a word count cannot be asked of every language.
        foreach (var (source, document) in parsed)
        {
            if (source.Meta.Lang == DocLang.Canonical)
            {
                SentenceLength.EnsureNoOverlongSentence(document, source.FileName);
            }
        }

        // Per language, so a link to a document that has no counterpart in the linking document's
        // language fails the build rather than resolving to a route that was never prerendered.
        var anchorsByLang = parsed
            .GroupBy(p => p.Source.Meta.Lang, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, IReadOnlySet<string>>)g.ToDictionary(
                    p => p.Source.Meta.Slug,
                    p => HeadingAnchors.Of(p.Document),
                    StringComparer.Ordinal),
                StringComparer.Ordinal);

        var docs = new List<(DocMeta Meta, string Html)>(sources.Count);
        foreach (var (source, document) in parsed)
        {
            docs.Add((
                source.Meta with { Anchors = HeadingAnchors.InOrder(document) },
                MarkdownConverter.ToHtml(
                    document,
                    anchorsByLang[source.Meta.Lang],
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
                // Anchors are empty here and filled in by pass 2, which is the first point at which
                // the document has been parsed and its headings are known.
                new DocMeta(
                    slug,
                    fields.Title,
                    DocDescription.Validate(fields.Description, lang, fileName),
                    fields.Order,
                    fields.Group,
                    lang,
                    Stale: false,
                    Anchors: []),
                fileName,
                body,
                fields.SourceHash));
        }

        EnsureGroupsAreContiguous(sources);
        return sources;
    }

    /// <summary>
    /// Requires one language's groups to run in <see cref="DocGroup.All"/> order once each, with no
    /// group interrupted by another.
    /// </summary>
    /// <remarks>
    /// Order stays the single ordering key: the rail reads a group heading off a run of documents
    /// rather than sorting by group and then by order. That keeps one number in front matter deciding
    /// one position, and it makes an interleaved group a build error instead of a rail that shows the
    /// same heading twice with different documents under each.
    /// </remarks>
    private static void EnsureGroupsAreContiguous(List<DocSource> sources)
    {
        var byOrder = sources.OrderBy(s => s.Meta.Order).ToList();
        for (int i = 1; i < byOrder.Count; i++)
        {
            var previous = byOrder[i - 1].Meta;
            var current = byOrder[i].Meta;
            if (DocGroup.Rank(current.Group) >= DocGroup.Rank(previous.Group))
            {
                continue;
            }

            throw Invalid(
                byOrder[i].FileName,
                $"group '{current.Group}' follows group '{previous.Group}' at order {current.Order}, " +
                $"but the navigation shows the groups in the order [{string.Join(", ", DocGroup.All)}]. " +
                "Renumber 'order' so each group's documents form one run.");
        }
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
        KeyValueBlock.Invalid(fileName, "document", reason);
}

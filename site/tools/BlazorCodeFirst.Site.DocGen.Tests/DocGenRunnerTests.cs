using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class DocGenRunnerTests
{
    private const string Intro =
        "---\ntitle: Getting Started\ndescription: D\norder: 10\ngroup: write\n---\n\n## Installation\n\n```csharp\nvar x = 1;\n```\n";

    private const string Second = "---\ntitle: Control Flow\ndescription: D\norder: 20\ngroup: write\n---\n\n## Loops\n";

    /// <summary>A shell file with every key a language of the given kind must declare.</summary>
    private static string Shell(string name, bool isCanonical)
    {
        string stale = isCanonical
            ? ""
            : "stale-notice: This translation is behind.\nstale-link: Read the English page\n";

        return "---\n" +
            $"name: {name}\n" +
            "index-title: Documentation\n" +
            "index-description: The guide, in reading order.\n" +
            "index-lead: Every document, in reading order.\n" +
            "rail-heading: Guide\n" +
            "language-label: Language\n" +
            "anchor-filter-label: Jump to a diagnostic\n" +
            "anchor-filter-count: {shown} of {total}\n" +
            "theme-toggle-name: Color theme: {state}\n" +
            "theme-toggle-label: Theme\n" +
            "theme-system: System\n" +
            "theme-light: Light\n" +
            "theme-dark: Dark\n" +
            "group-start: Start here\n" +
            "group-write: Writing views\n" +
            "group-reference: Reference\n" +
            stale +
            "---\n";
    }

    /// <summary>Runs the body with a temp content directory and output paths under a nested
    /// directory, which also proves Run creates missing output directories.</summary>
    /// <remarks>
    /// The canonical shell file is seeded here because every test below has canonical documents and
    /// none of them is about the shell. The tests that ARE about it write their own.
    /// </remarks>
    private static void WithContent(Action<string, string, string> body)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string content = Path.Combine(dir, "content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "shell.yml"), Shell("English", isCanonical: true));
            body(content, Path.Combine(dir, "gen", "Docs.g.cs"), Path.Combine(dir, "gen", "highlight.css"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Writes a translation under the 'ja' subdirectory, creating it on first use.</summary>
    /// <remarks>
    /// The fixtures below are ASCII, and must stay that way. What is under test is the language axis
    /// -- the directory a file sits in, the counterpart it records, the slug set its links resolve
    /// against -- and none of that reads a character. Writing the fixtures in Japanese would buy no
    /// coverage and would trip the CI scan that keeps CJK out of site/**/*.cs, whose one exception is
    /// site/content/ja/.
    /// </remarks>
    private static void WriteJa(string content, string fileName, string text)
    {
        string dir = Path.Combine(content, "ja");
        Directory.CreateDirectory(dir);

        string shell = Path.Combine(dir, "shell.yml");
        if (!File.Exists(shell))
        {
            File.WriteAllText(shell, Shell("Japanese", isCanonical: false));
        }

        File.WriteAllText(Path.Combine(dir, fileName), text);
    }

    /// <summary>
    /// The hash DocGen reports for a stale translation, which is the value an author pastes back.
    /// Deliberately obtained from a run rather than recomputed here: a test that hashes the document
    /// itself would agree with a broken implementation, because it would be the same arithmetic.
    /// </summary>
    private static string ExpectedHashFrom(string report)
    {
        const string Marker = "'source-hash: ";
        int start = report.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the stale report must name the expected hash, but was: {report}");
        start += Marker.Length;
        return report[start..report.IndexOf('\'', start)];
    }

    [Fact]
    public void Run_ConvertsMarkdownToCommittableArtifacts()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);

            DocGenRunner.Run(content, docsOut, cssOut);

            string docs = File.ReadAllText(docsOut);
            Assert.Contains("public static readonly ImmutableArray<DocEntry> All", docs);
            Assert.Contains("\"getting-started\"", docs);
            Assert.Contains("\"Getting Started\"", docs);
            Assert.Contains("id=\\\"installation\\\"", docs);  // escaped in the literal
            Assert.Contains("class=\\\"csharp\\\"", docs);     // highlighting survived
            Assert.DoesNotContain("\r\n", docs);
            Assert.Contains(".keyword", File.ReadAllText(cssOut));
        });
    }

    /// <summary>
    /// A run whose output is byte-identical must leave both artifacts' timestamps alone.
    /// </summary>
    /// <remarks>
    /// GeneratedFile's own remarks say what the site build reads that timestamp for. The assertion
    /// sets it into the past rather than comparing two readings, so it does not depend on the
    /// filesystem's timestamp resolution.
    /// </remarks>
    [Fact]
    public void Run_ArtifactsUnchanged_DoesNotRewriteThem()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            var before = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(docsOut, before);
            File.SetLastWriteTimeUtc(cssOut, before);

            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            Assert.Equal(before, File.GetLastWriteTimeUtc(docsOut));
            Assert.Equal(before, File.GetLastWriteTimeUtc(cssOut));
        });
    }

    /// <summary>The other half of the pair above: skipping an identical write must not turn into
    /// skipping a write. A stale artifact left on disk is what the build would then compile.</summary>
    [Fact]
    public void Run_ContentChanged_RewritesTheArtifact()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            File.WriteAllText(Path.Combine(content, "control-flow.md"), Second);
            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            Assert.Contains("\"control-flow\"", File.ReadAllText(docsOut), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_FrontMatterNeverLeaksIntoHtml()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);

            DocGenRunner.Run(content, docsOut, cssOut);

            string docs = File.ReadAllText(docsOut);
            Assert.DoesNotContain("title:", docs);
            Assert.DoesNotContain("order:", docs);
        });
    }

    [Fact]
    public void Run_OrdersDocumentsByFrontMatterOrder()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            // File name order (control-flow < getting-started) is the reverse of front matter order.
            File.WriteAllText(Path.Combine(content, "control-flow.md"), Second);
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);

            DocGenRunner.Run(content, docsOut, cssOut);

            string docs = File.ReadAllText(docsOut);
            Assert.True(
                docs.IndexOf("\"getting-started\"", StringComparison.Ordinal)
                    < docs.IndexOf("\"control-flow\"", StringComparison.Ordinal),
                "documents must be emitted in front matter order, not file name order");
        });
    }

    [Fact]
    public void Run_InvalidFileNameSlug_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "Getting_Started.md"), Intro);

            var ex = Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));
            Assert.Contains("Getting_Started.md", ex.Message);
        });
    }

    [Fact]
    public void Run_BodyWithH1_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\ndescription: D\norder: 1\ngroup: write\n---\n\n# Nope\n");

            var ex = Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));
            Assert.Contains("a.md", ex.Message);
        });
    }

    [Fact]
    public void Run_DuplicateOrder_ThrowsNamingBothFiles()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\ndescription: D\norder: 10\ngroup: write\n---\n\n## A\n");
            File.WriteAllText(Path.Combine(content, "b.md"), "---\ntitle: B\ndescription: D\norder: 10\ngroup: write\n---\n\n## B\n");

            var ex = Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));

            // Both halves of the collision must be named, so the author does not have to search the
            // content directory for the other file.
            Assert.Contains("a.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("b.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("10", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_GroupsRunningInNavigationOrder_IsAllowed()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\ndescription: D\norder: 10\ngroup: start\n---\n\n## A\n");
            File.WriteAllText(Path.Combine(content, "b.md"), "---\ntitle: B\ndescription: D\norder: 20\ngroup: write\n---\n\n## B\n");
            File.WriteAllText(Path.Combine(content, "c.md"), "---\ntitle: C\ndescription: D\norder: 30\ngroup: write\n---\n\n## C\n");
            File.WriteAllText(Path.Combine(content, "d.md"), "---\ntitle: D\ndescription: D\norder: 40\ngroup: reference\n---\n\n## D\n");

            DocGenRunner.Run(content, docsOut, cssOut);

            string source = File.ReadAllText(docsOut);
            Assert.Contains("\"start\"", source, StringComparison.Ordinal);
            Assert.Contains("\"reference\"", source, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_AGroupInterruptedByAnother_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            // 'write' at 20, then 'start' again at 30. The rail reads a heading off a run of
            // documents, so an interleaved group would show one heading twice.
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\ndescription: D\norder: 10\ngroup: start\n---\n\n## A\n");
            File.WriteAllText(Path.Combine(content, "b.md"), "---\ntitle: B\ndescription: D\norder: 20\ngroup: write\n---\n\n## B\n");
            File.WriteAllText(Path.Combine(content, "c.md"), "---\ntitle: C\ndescription: D\norder: 30\ngroup: start\n---\n\n## C\n");

            var ex = Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));

            Assert.Contains("c.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("start", ex.Message, StringComparison.Ordinal);
            Assert.Contains("write", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TranslationGroupsAreCheckedSeparately()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\ndescription: D\norder: 10\ngroup: start\n---\n\n## A\n");
            File.WriteAllText(Path.Combine(content, "b.md"), "---\ntitle: B\ndescription: D\norder: 20\ngroup: write\n---\n\n## B\n");

            // The translation carries its own order numbers, so it can interleave on its own. The
            // hashes are placeholders: the group check runs in pass 1, before staleness is resolved,
            // so a stale one cannot reach this assertion first.
            WriteJa(content, "a.md", "---\ntitle: A\ndescription: D\norder: 10\ngroup: write\nsource-hash: 00000000\n---\n\n## A\n");
            WriteJa(content, "b.md", "---\ntitle: B\ndescription: D\norder: 20\ngroup: start\nsource-hash: 00000000\n---\n\n## B\n");

            var ex = Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));

            Assert.Contains("ja/b.md", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_DuplicateOrder_FailsBeforeWritingAnyArtifact()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\ndescription: D\norder: 10\ngroup: write\n---\n\n## A\n");
            File.WriteAllText(Path.Combine(content, "b.md"), "---\ntitle: B\ndescription: D\norder: 10\ngroup: write\n---\n\n## B\n");

            Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));

            // Validation precedes conversion and emission: a rejected content set leaves no artifact.
            Assert.False(File.Exists(docsOut));
            Assert.False(File.Exists(cssOut));
        });
    }

    [Fact]
    public void Run_DescriptionOverTheCanonicalLimit_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(
                Path.Combine(content, "intro.md"),
                "---\ntitle: Intro\ndescription: " + new string('x', 161) +
                    "\norder: 10\ngroup: write\n---\n\n## Body\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut));

            Assert.Contains("description", ex.Message, StringComparison.Ordinal);
            Assert.Contains("161", ex.Message, StringComparison.Ordinal);
            Assert.Contains("intro.md", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TranslationDescriptionOverItsOwnLimit_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            // A length the canonical edition would accept, rejected in a translation. The limit is
            // decided by the directory the file sits in, which is the same thing that decides its
            // language.
            File.WriteAllText(Path.Combine(content, "intro.md"), Intro);
            WriteJa(
                content,
                "intro.md",
                "---\ntitle: Intro (ja)\ndescription: " + new string('x', 91) +
                    "\norder: 10\ngroup: write\nsource-hash: 00000000\n---\n\n## Body\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            Assert.Contains("91", ex.Message, StringComparison.Ordinal);
            Assert.Contains("90", ex.Message, StringComparison.Ordinal);
            Assert.Contains("ja/intro.md", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_CarriesEachDocumentsDescriptionIntoTheManifest()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(
                Path.Combine(content, "intro.md"),
                "---\ntitle: Intro\ndescription: What this page answers.\norder: 10\ngroup: write\n---\n\n## Body\n");

            DocGenRunner.Run(content, docsOut, cssOut);

            Assert.Contains(
                "\"What this page answers.\"",
                File.ReadAllText(docsOut),
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);

            DocGenRunner.Run(content, docsOut, cssOut);
            string first = File.ReadAllText(docsOut);
            DocGenRunner.Run(content, docsOut, cssOut);

            Assert.Equal(first, File.ReadAllText(docsOut));
        });
    }

    [Fact]
    public void Run_LanguageWithDocumentsButNoShellFile_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            string dir = Path.Combine(content, "ja");
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "getting-started.md"),
                "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            Assert.Contains("ja/shell.yml", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_LanguageWithNoDocuments_NeedsNoShellFile()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            // An edition nobody has started is not an unfinished one: nothing routes to it, so there
            // is no page for the missing strings to have shown on.
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            Directory.CreateDirectory(Path.Combine(content, "ja"));

            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            Assert.Contains("Languages =\n        [\"en\"]", File.ReadAllText(docsOut), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_CarriesEachLanguagesShellTextIntoTheManifest()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n");

            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            // Every reader-facing string reaches the site through the manifest, so no page component
            // has to hold a sentence in any language.
            string docs = File.ReadAllText(docsOut);
            Assert.Contains("public static ShellText Shell(string lang)", docs, StringComparison.Ordinal);
            Assert.Contains("\"Japanese\"", docs, StringComparison.Ordinal);
            Assert.Contains("\"This translation is behind.\"", docs, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TranslationTakesItsLanguageFromTheDirectory()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: 00000000\n---\n\n## Installation\n");

            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            string docs = File.ReadAllText(docsOut);
            Assert.Contains("\"ja\",", docs, StringComparison.Ordinal);

            // Nothing in the file declares its language: the directory is the only thing that says so.
            Assert.DoesNotContain("lang:", docs, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_UnknownSubdirectory_ThrowsNamingIt()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            string drafts = Path.Combine(content, "drafts");
            Directory.CreateDirectory(drafts);
            File.WriteAllText(Path.Combine(drafts, "a.md"), "---\ntitle: A\ndescription: D\norder: 99\ngroup: write\n---\n\n## A\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            // Before languages existed this directory was skipped in silence, which is the failure
            // this rejection exists to end.
            Assert.Contains("drafts", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TranslationWithNoCanonicalDocument_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            WriteJa(content, "orphan.md", "---\ntitle: Orphan\ndescription: D\norder: 90\ngroup: write\nsource-hash: 00000000\n---\n\n## Orphan\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            Assert.Contains("ja/orphan.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("orphan.md", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TranslationMissingSourceHash_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\n---\n\n## Installation\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            // An absent hash must not default to "fresh": that would silently disable the check for
            // the one document nobody is watching.
            Assert.Contains("ja/getting-started.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("source-hash", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_CanonicalDocumentDeclaringSourceHash_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(
                Path.Combine(content, "getting-started.md"),
                "---\ntitle: Getting Started\ndescription: D\norder: 10\ngroup: write\nsource-hash: 00000000\n---\n\n## Installation\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            Assert.Contains("getting-started.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("source-hash", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_OutdatedSourceHash_MarksStaleAndReportsTheExpectedHash()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n");

            var report = new StringWriter();
            DocGenRunner.Run(content, docsOut, cssOut, report);

            // Drift is reported, not fatal: an English edit must not oblige the same commit to rewrite
            // every translation.
            string docs = File.ReadAllText(docsOut);
            Assert.Contains("\"ja\",\n            true,", docs, StringComparison.Ordinal);
            Assert.Contains("ja/getting-started.md", report.ToString(), StringComparison.Ordinal);
            Assert.NotEqual("deadbeef", ExpectedHashFrom(report.ToString()));
        });
    }

    [Fact]
    public void Run_SourceHashFromTheReport_ClearsStale()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n");

            var report = new StringWriter();
            DocGenRunner.Run(content, docsOut, cssOut, report);
            string expected = ExpectedHashFrom(report.ToString());

            // The author's actual workflow: revise the translation, then paste the hash the tool named.
            WriteJa(content, "getting-started.md", $"---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: {expected}\n---\n\n## Installation\n");
            var second = new StringWriter();
            DocGenRunner.Run(content, docsOut, cssOut, second);

            Assert.Equal("", second.ToString());
            Assert.Contains("\"ja\",\n            false,", File.ReadAllText(docsOut), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_CanonicalOrderChange_DoesNotMarkTranslationsStale()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n");

            var first = new StringWriter();
            DocGenRunner.Run(content, docsOut, cssOut, first);
            string expected = ExpectedHashFrom(first.ToString());
            WriteJa(content, "getting-started.md", $"---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: {expected}\n---\n\n## Installation\n");

            // Renumbering the navigation changes no word a reader sees, so it must not send every
            // translator back to a document that reads exactly as it did.
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro.Replace("order: 10", "order: 15", StringComparison.Ordinal));
            WriteJa(content, "getting-started.md", $"---\ntitle: Getting Started (ja)\ndescription: D\norder: 15\ngroup: write\nsource-hash: {expected}\n---\n\n## Installation\n");

            var second = new StringWriter();
            DocGenRunner.Run(content, docsOut, cssOut, second);

            Assert.Equal("", second.ToString());
        });
    }

    [Fact]
    public void Run_SameOrderInTwoLanguages_IsAllowed()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);

            // A translation sits at the same position in its own navigation as the document it
            // translates, so sharing order 10 across languages is the intended arrangement.
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n");

            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            Assert.Contains("\"ja\",", File.ReadAllText(docsOut), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_DuplicateOrderWithinOneTranslationLanguage_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            File.WriteAllText(Path.Combine(content, "control-flow.md"), Second);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n");
            WriteJa(content, "control-flow.md", "---\ntitle: Control Flow (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Loops\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            Assert.Contains("ja/control-flow.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("ja/getting-started.md", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_RelativeLinkInATranslation_ResolvesWithinItsOwnLanguage()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            File.WriteAllText(Path.Combine(content, "control-flow.md"), Second);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n\n[Next](./control-flow.md)\n");
            WriteJa(content, "control-flow.md", "---\ntitle: Control Flow (ja)\ndescription: D\norder: 20\ngroup: write\nsource-hash: deadbeef\n---\n\n## Loops\n");

            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            string docs = File.ReadAllText(docsOut);
            Assert.Contains("/docs/ja/control-flow", docs, StringComparison.Ordinal);

            // The reader must not be dropped back into English by following a link inside the
            // Japanese edition.
            Assert.DoesNotContain("href=\\\"/docs/control-flow\\\"", docs, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TranslationLinkingToAnUntranslatedDocument_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            File.WriteAllText(Path.Combine(content, "control-flow.md"), Second);

            // control-flow has no Japanese counterpart, so /docs/ja/control-flow is a route that was
            // never generated and never prerendered.
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n\n[Next](./control-flow.md)\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            Assert.Contains("ja/getting-started.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("control-flow", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TranslationLinkingToASectionOnlyTheEnglishDocumentHas_Throws()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            File.WriteAllText(Path.Combine(content, "control-flow.md"), Second);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n\n[Next](./control-flow.md#loops)\n");

            // The English document publishes "loops"; its translation publishes "repetition", the way
            // a real translation publishes a translated heading. A fragment resolves in the linking
            // document's own language, the same way its document half does, so a translator who
            // copied the English link is told rather than shipping a link that lands at the top of
            // the page. (The heading here is English because site.yml scans this file for Japanese;
            // MarkdownConverterTests holds the non-ASCII half.)
            WriteJa(content, "control-flow.md", "---\ntitle: Control Flow (ja)\ndescription: D\norder: 20\ngroup: write\nsource-hash: deadbeef\n---\n\n## Repetition\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            Assert.Contains("ja/getting-started.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("loops", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TranslationLinkingToASectionItsOwnEditionPublishes_Resolves()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            File.WriteAllText(Path.Combine(content, "control-flow.md"), Second);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\ndescription: D\norder: 10\ngroup: write\nsource-hash: deadbeef\n---\n\n## Installation\n\n[Next](./control-flow.md#repetition)\n");
            WriteJa(content, "control-flow.md", "---\ntitle: Control Flow (ja)\ndescription: D\norder: 20\ngroup: write\nsource-hash: deadbeef\n---\n\n## Repetition\n");

            DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null);

            Assert.Contains(
                "/docs/ja/control-flow#repetition", File.ReadAllText(docsOut), StringComparison.Ordinal);
        });
    }
}

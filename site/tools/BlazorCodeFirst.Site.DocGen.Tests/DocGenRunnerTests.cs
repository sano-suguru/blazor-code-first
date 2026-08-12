using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class DocGenRunnerTests
{
    private const string Intro =
        "---\ntitle: Getting Started\norder: 10\n---\n\n## Installation\n\n```csharp\nvar x = 1;\n```\n";

    private const string Second = "---\ntitle: Control Flow\norder: 20\n---\n\n## Loops\n";

    /// <summary>Runs the body with a temp content directory and output paths under a nested
    /// directory, which also proves Run creates missing output directories.</summary>
    private static void WithContent(Action<string, string, string> body)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string content = Path.Combine(dir, "content");
            Directory.CreateDirectory(content);
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
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\norder: 1\n---\n\n# Nope\n");

            var ex = Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));
            Assert.Contains("a.md", ex.Message);
        });
    }

    [Fact]
    public void Run_DuplicateOrder_ThrowsNamingBothFiles()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\norder: 10\n---\n\n## A\n");
            File.WriteAllText(Path.Combine(content, "b.md"), "---\ntitle: B\norder: 10\n---\n\n## B\n");

            var ex = Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));

            // Both halves of the collision must be named, so the author does not have to search the
            // content directory for the other file.
            Assert.Contains("a.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("b.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("10", ex.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_DuplicateOrder_FailsBeforeWritingAnyArtifact()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "a.md"), "---\ntitle: A\norder: 10\n---\n\n## A\n");
            File.WriteAllText(Path.Combine(content, "b.md"), "---\ntitle: B\norder: 10\n---\n\n## B\n");

            Assert.Throws<InvalidOperationException>(() => DocGenRunner.Run(content, docsOut, cssOut));

            // Validation precedes conversion and emission: a rejected content set leaves no artifact.
            Assert.False(File.Exists(docsOut));
            Assert.False(File.Exists(cssOut));
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
    public void Run_TranslationTakesItsLanguageFromTheDirectory()
    {
        WithContent((content, docsOut, cssOut) =>
        {
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro);
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: 00000000\n---\n\n## Installation\n");

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
            File.WriteAllText(Path.Combine(drafts, "a.md"), "---\ntitle: A\norder: 99\n---\n\n## A\n");

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
            WriteJa(content, "orphan.md", "---\ntitle: Orphan\norder: 90\nsource-hash: 00000000\n---\n\n## Orphan\n");

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
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\n---\n\n## Installation\n");

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
                "---\ntitle: Getting Started\norder: 10\nsource-hash: 00000000\n---\n\n## Installation\n");

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
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: deadbeef\n---\n\n## Installation\n");

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
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: deadbeef\n---\n\n## Installation\n");

            var report = new StringWriter();
            DocGenRunner.Run(content, docsOut, cssOut, report);
            string expected = ExpectedHashFrom(report.ToString());

            // The author's actual workflow: revise the translation, then paste the hash the tool named.
            WriteJa(content, "getting-started.md", $"---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: {expected}\n---\n\n## Installation\n");
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
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: deadbeef\n---\n\n## Installation\n");

            var first = new StringWriter();
            DocGenRunner.Run(content, docsOut, cssOut, first);
            string expected = ExpectedHashFrom(first.ToString());
            WriteJa(content, "getting-started.md", $"---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: {expected}\n---\n\n## Installation\n");

            // Renumbering the navigation changes no word a reader sees, so it must not send every
            // translator back to a document that reads exactly as it did.
            File.WriteAllText(Path.Combine(content, "getting-started.md"), Intro.Replace("order: 10", "order: 15", StringComparison.Ordinal));
            WriteJa(content, "getting-started.md", $"---\ntitle: Getting Started (ja)\norder: 15\nsource-hash: {expected}\n---\n\n## Installation\n");

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
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: deadbeef\n---\n\n## Installation\n");

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
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: deadbeef\n---\n\n## Installation\n");
            WriteJa(content, "control-flow.md", "---\ntitle: Control Flow (ja)\norder: 10\nsource-hash: deadbeef\n---\n\n## Loops\n");

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
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: deadbeef\n---\n\n## Installation\n\n[Next](./control-flow.md)\n");
            WriteJa(content, "control-flow.md", "---\ntitle: Control Flow (ja)\norder: 20\nsource-hash: deadbeef\n---\n\n## Loops\n");

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
            WriteJa(content, "getting-started.md", "---\ntitle: Getting Started (ja)\norder: 10\nsource-hash: deadbeef\n---\n\n## Installation\n\n[Next](./control-flow.md)\n");

            var ex = Assert.Throws<InvalidOperationException>(
                () => DocGenRunner.Run(content, docsOut, cssOut, TextWriter.Null));

            Assert.Contains("ja/getting-started.md", ex.Message, StringComparison.Ordinal);
            Assert.Contains("control-flow", ex.Message, StringComparison.Ordinal);
        });
    }
}

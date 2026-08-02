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
}

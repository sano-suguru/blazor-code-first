using BlazorCompose.Site.DocGen;
using Xunit;

namespace BlazorCompose.Site.DocGen.Tests;

public class DocGenRunnerTests
{
    [Fact]
    public void Run_ConvertsMarkdownToCommittableArtifacts()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string content = Path.Combine(dir, "content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "getting-started.md"),
                "# Getting Started\n\n```csharp\nvar x = 1;\n```\n");

            string docsOut = Path.Combine(dir, "Docs.g.cs");
            string cssOut = Path.Combine(dir, "highlight.css");

            DocGenRunner.Run(content, docsOut, cssOut);

            string docs = File.ReadAllText(docsOut);
            Assert.Contains("public const string GettingStarted =", docs);
            Assert.Contains("id=\\\"getting-started\\\"", docs); // escaped in the literal
            Assert.DoesNotContain("\r\n", docs);

            string css = File.ReadAllText(cssOut);
            Assert.Contains(".keyword", css);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Run_CreatesMissingOutputDirectories()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string content = Path.Combine(dir, "content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "getting-started.md"),
                "# Getting Started\n");

            // Neither output's parent directory exists yet.
            string docsOut = Path.Combine(dir, "generated", "docs", "Docs.g.cs");
            string cssOut = Path.Combine(dir, "generated", "css", "highlight.css");

            DocGenRunner.Run(content, docsOut, cssOut);

            Assert.True(File.Exists(docsOut));
            Assert.True(File.Exists(cssOut));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Run_IsDeterministic_AcrossEnumerationOrder()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string content = Path.Combine(dir, "content");
            Directory.CreateDirectory(content);
            File.WriteAllText(Path.Combine(content, "zebra.md"), "# Zebra\n");
            File.WriteAllText(Path.Combine(content, "apple.md"), "# Apple\n");

            string docsOut = Path.Combine(dir, "Docs.g.cs");
            string cssOut = Path.Combine(dir, "highlight.css");

            DocGenRunner.Run(content, docsOut, cssOut);
            string docs = File.ReadAllText(docsOut);

            // Members are ordinal-sorted regardless of filesystem enumeration order.
            Assert.True(docs.IndexOf("Apple", StringComparison.Ordinal)
                < docs.IndexOf("Zebra", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

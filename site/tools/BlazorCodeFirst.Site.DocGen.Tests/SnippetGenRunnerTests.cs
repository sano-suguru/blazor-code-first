using BlazorCodeFirst.Site.DocGen;
using Xunit;

namespace BlazorCodeFirst.Site.DocGen.Tests;

public class SnippetGenRunnerTests
{
    /// <summary>Runs the body with a temp snippets directory and an output path under a nested
    /// directory, which also proves Run creates missing output directories.</summary>
    private static void WithSnippets(Action<string, string> body)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string snippets = Path.Combine(dir, "snippets");
            Directory.CreateDirectory(snippets);
            body(snippets, Path.Combine(dir, "gen", "Snippets.g.cs"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Run_DeclaredSnippet_WritesItsHighlightedHtmlAsAConst()
    {
        WithSnippets((snippets, outPath) =>
        {
            File.WriteAllText(Path.Combine(snippets, "manifest"), "---\nhero: hero.cs\n---\n");
            File.WriteAllText(Path.Combine(snippets, "hero.cs"), "var x = 1;\n");

            SnippetGenRunner.Run(snippets, outPath);

            string code = File.ReadAllText(outPath);
            Assert.Contains("public const string Hero = ", code, StringComparison.Ordinal);
            Assert.Contains("<span class=\\\"keyword\\\">var</span>", code, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_PathLeavingTheSnippetsDirectory_ReadsThatFile()
    {
        WithSnippets((snippets, outPath) =>
        {
            string outside = Path.Combine(Path.GetDirectoryName(snippets)!, "Page.cs");
            File.WriteAllText(outside, "var outside = 1;\n");
            File.WriteAllText(Path.Combine(snippets, "manifest"), "---\ncounter: ../Page.cs\n---\n");

            SnippetGenRunner.Run(snippets, outPath);

            Assert.Contains("outside", File.ReadAllText(outPath), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_DeclaredFileThatDoesNotExist_ThrowsNamingTheSnippetAndThePath()
    {
        WithSnippets((snippets, outPath) =>
        {
            File.WriteAllText(Path.Combine(snippets, "manifest"), "---\nhero: missing.cs\n---\n");

            var error = Assert.Throws<InvalidOperationException>(
                () => SnippetGenRunner.Run(snippets, outPath));

            Assert.Contains("hero", error.Message, StringComparison.Ordinal);
            Assert.Contains("missing.cs", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_NoManifest_ThrowsNamingTheFileItLookedFor()
    {
        WithSnippets((snippets, outPath) =>
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => SnippetGenRunner.Run(snippets, outPath));

            Assert.Contains("manifest", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Run_TwiceOverTheSameInput_WritesTheSameBytes()
    {
        WithSnippets((snippets, outPath) =>
        {
            File.WriteAllText(
                Path.Combine(snippets, "manifest"), "---\nhero: hero.cs\nzed: zed.cs\n---\n");
            File.WriteAllText(Path.Combine(snippets, "hero.cs"), "var x = 1;\n");
            File.WriteAllText(Path.Combine(snippets, "zed.cs"), "var y = 2;\n");

            SnippetGenRunner.Run(snippets, outPath);
            byte[] first = File.ReadAllBytes(outPath);
            SnippetGenRunner.Run(snippets, outPath);

            Assert.Equal(first, File.ReadAllBytes(outPath));
        });
    }

    [Fact]
    public void Run_Always_WritesUtf8WithoutABom()
    {
        WithSnippets((snippets, outPath) =>
        {
            File.WriteAllText(Path.Combine(snippets, "manifest"), "---\nhero: hero.cs\n---\n");
            File.WriteAllText(Path.Combine(snippets, "hero.cs"), "// é\nvar x = 1;\n");

            SnippetGenRunner.Run(snippets, outPath);

            byte[] bytes = File.ReadAllBytes(outPath);
            Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);
        });
    }
}

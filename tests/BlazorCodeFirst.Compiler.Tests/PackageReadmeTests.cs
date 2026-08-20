namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Keeps the package README's two code blocks honest about each other. The README is the whole
/// nuget.org page, and its one piece of evidence for the claim it opens with is that the second block
/// really is what the generator emits for the first. Nothing else would catch that pair going stale:
/// the README is prose to every other check in the repository, so an emitter change would quietly
/// leave a wrong render tree published on nuget.org.
/// </summary>
/// <remarks>
/// The blocks are found by the HTML comments that precede them, which markdown renderers hide, rather
/// than by position, so the README can gain or reorder code blocks without silently re-pointing this
/// test at a different pair.
/// </remarks>
public sealed class PackageReadmeTests
{
    private static readonly string ReadmePath = Path.Combine(
        GeneratedSourceSnapshot.FindRepositoryRoot(),
        "src",
        "BlazorCodeFirst.Runtime",
        "README.md");

    [Fact]
    public void PackageReadme_GeneratedBlock_IsWhatTheGeneratorEmitsForTheInputBlock()
    {
        var readme = File.ReadAllText(ReadmePath).Replace("\r\n", "\n");
        var component = ReadBlock(readme, "input");
        var expected = ReadBlock(readme, "generated");

        var result = CompilationTestHost.RunGenerator(component);
        CompilationTestHost.AssertOutputCompiles(result);

        var generated = Assert.Single(result.GeneratedSources).SourceText.ToString().Replace("\r\n", "\n");
        Assert.Contains(expected, generated);
    }

    /// <summary>
    /// Returns the fenced block introduced by <c>&lt;!-- readme-example: <paramref name="name"/> --&gt;</c>.
    /// </summary>
    private static string ReadBlock(string readme, string name)
    {
        var marker = $"<!-- readme-example: {name} -->";
        var markerIndex = readme.IndexOf(marker, System.StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"README.md has no '{marker}' marker.");

        const string fence = "```";
        var openIndex = readme.IndexOf(fence, markerIndex, System.StringComparison.Ordinal);
        Assert.True(openIndex >= 0, $"README.md has no code fence after '{marker}'.");

        var contentIndex = readme.IndexOf('\n', openIndex) + 1;
        var closeIndex = readme.IndexOf("\n" + fence, contentIndex, System.StringComparison.Ordinal);
        Assert.True(closeIndex >= 0, $"README.md has an unterminated code fence after '{marker}'.");

        return readme[contentIndex..closeIndex];
    }
}

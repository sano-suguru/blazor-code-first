namespace BlazorCodeFirst.Site.DocGen;

/// <summary>Orchestrates snippet generation and writes the committed Snippets.g.cs.</summary>
/// <remarks>
/// A runner of its own rather than parameters on <see cref="DocGenRunner"/>. It reads a different
/// tree and writes a different artifact, and the two share only the conversion pipeline underneath.
/// Keeping them apart is also what lets the documents' rule that every content subdirectory names a
/// language stay enforced, because snippets never enter that tree.
/// </remarks>
public static class SnippetGenRunner
{
    public static void Run(string snippetsDir, string outPath)
    {
        ArgumentNullException.ThrowIfNull(snippetsDir);
        ArgumentNullException.ThrowIfNull(outPath);

        string manifestPath = Path.Combine(snippetsDir, SnippetManifest.FileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"The snippets directory '{snippetsDir}' has no " +
                $"'{SnippetManifest.FileName}' file. That file declares which sources become figures.");
        }

        var entries = SnippetManifest.Parse(
            File.ReadAllText(manifestPath), SnippetManifest.FileName);

        var converted = new List<(SnippetEntry Entry, string Html)>(entries.Count);
        foreach (var entry in entries)
        {
            string path = Path.GetFullPath(Path.Combine(snippetsDir, entry.Path));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Snippet '{entry.Name}' declares '{entry.Path}', which does not exist " +
                    $"(resolved to '{path}').");
            }

            converted.Add((entry, SnippetConverter.ToHtml(File.ReadAllText(path), path, entry.Name)));
        }

        GeneratedFile.Write(outPath, CSharpSnippetEmitter.Emit(converted));
    }
}

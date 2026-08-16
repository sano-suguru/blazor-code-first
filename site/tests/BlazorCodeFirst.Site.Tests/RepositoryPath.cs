namespace BlazorCodeFirst.Site.Tests;

/// <summary>Resolves a repository-relative path from a test running in bin/.</summary>
/// <remarks>
/// The subjects here are files in the working tree rather than embedded resources, because what is
/// under test is what an author committed. Reading a copy the build placed beside the assembly
/// would assert the build's own output, which is the mistake #278 records.
/// </remarks>
internal static class RepositoryPath
{
    public static string From(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlazorCodeFirst.slnx")))
            {
                return Path.Combine(directory.FullName, relativePath);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No directory above '{AppContext.BaseDirectory}' holds BlazorCodeFirst.slnx.");
    }
}

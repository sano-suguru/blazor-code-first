using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Compares a generator run's complete emitted source against a committed baseline.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the other generator assertions are substring-based: they check that an expected
/// <c>__builder</c> call appears, which cannot see an added frame, a reordered attribute, or a skipped
/// sequence number.  A full-text baseline pins all three, which is what makes it usable as the safety net
/// for a change that rewrites every call site (#87) rather than the emitter.
/// </para>
/// <para>
/// Baselines are committed under <see cref="DirectoryName"/> as <c>&lt;case&gt;.expected.cs</c> and are
/// excluded from compilation by the project file — they declare the same types as the inputs that produced
/// them, so compiling them is a duplicate-definition error.  The extension is deliberately not
/// <c>.g.cs</c>: the emitted text opens with <c>// &lt;auto-generated/&gt;</c>, and that plus a
/// <c>.g.cs</c> name invites review tools to collapse the file.  The point of committing a baseline is that
/// an intentional change to it is visible in the diff, so a collapsed diff would defeat it.
/// </para>
/// </remarks>
public static class GeneratedSourceSnapshot
{
    /// <summary>The baseline directory, relative to the repository root.</summary>
    private const string DirectoryName = "tests/BlazorCompose.Compiler.Tests/Snapshots";

    /// <summary>Set to <c>1</c> to rewrite baselines instead of only reporting the difference.</summary>
    private const string UpdateVariable = "BLAZORCOMPOSE_UPDATE_SNAPSHOTS";

    /// <summary>Separates one generated file from the next when a run emits several.</summary>
    private const string FileSeparator = "// ===== ";

    /// <summary>
    /// Asserts that <paramref name="result"/>'s generated sources match the baseline for
    /// <paramref name="caseName"/>, writing the baseline first when <see cref="UpdateVariable"/> is set.
    /// </summary>
    public static void Verify(string caseName, GeneratorRunResult result)
    {
        var actual = Normalize(Render(result));
        var path = Path.Combine(BaselineDirectory, caseName + ".expected.cs");

        var existed = File.Exists(path);
        var expected = existed ? Normalize(File.ReadAllText(path)) : null;

        if (expected == actual)
            return;

        // Writing and then failing is deliberate. A helper that wrote the file and passed would let an
        // unreviewed change ride along in any run that happened to have the variable set, and the whole
        // value of the baseline is that changing it is a visible, deliberate act.
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(BaselineDirectory);
            File.WriteAllText(path, actual + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail(
                $"Baseline '{caseName}' was {(existed ? "updated" : "created")} because {UpdateVariable} " +
                "is set. Review the diff and re-run without the variable.");
        }

        Assert.Fail(
            existed
                ? $"Generated source for '{caseName}' does not match its baseline.\n" +
                  $"{DescribeFirstDifference(expected!, actual)}\n" +
                  $"Re-run with {UpdateVariable}=1 to rewrite it, then review the diff."
                : $"No baseline for '{caseName}' at {DirectoryName}/{caseName}.expected.cs.\n" +
                  $"Re-run with {UpdateVariable}=1 to create it, then review the diff.");
    }

    /// <summary>The case names every committed baseline belongs to, so an orphan can be detected.</summary>
    public static IEnumerable<string> EnumerateBaselineCaseNames()
    {
        const string suffix = ".expected.cs";

        return Directory.Exists(BaselineDirectory)
            ? Directory.EnumerateFiles(BaselineDirectory, "*" + suffix)
                .Select(static path => Path.GetFileName(path))
                .Select(name => name[..^suffix.Length])
                .OrderBy(static name => name, StringComparer.Ordinal)
            : [];
    }

    // ---------------------------------------------------------------------------

    private static readonly string BaselineDirectory =
        Path.Combine(FindRepositoryRoot(), DirectoryName.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Walks up from the test assembly's location to the directory holding the solution.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>[CallerFilePath]</c>. <c>Directory.Build.props</c> sets
    /// <c>ContinuousIntegrationBuild</c> when <c>CI</c> is set, which turns on
    /// <c>DeterministicSourcePaths</c> and hence <c>PathMap</c>; Roslyn applies <c>PathMap</c> to
    /// <c>CallerFilePath</c> too, so the compiled-in path is <c>/_/tests/...</c> on CI and the real
    /// checkout path locally.  Every case would pass locally and fail on CI with a misleading "baseline
    /// missing".  <c>BlazorCompose.DiagnosticTests.RepoLayout</c> does the same walk for the same reason;
    /// it is not shared because coupling the two test projects for fifteen lines costs more than it saves.
    /// </remarks>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BlazorCompose.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate BlazorCompose.slnx above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Renders every generated file, ordered by hint name and each introduced by its own header, so a run
    /// that emits more than one file is captured whole rather than reduced to whichever file happened to
    /// come first.  Ordering by hint name also makes the baseline independent of the order the generator
    /// happened to add sources in.
    /// </summary>
    private static string Render(GeneratorRunResult result)
    {
        var builder = new StringBuilder();

        foreach (var generated in result.GeneratedSources.OrderBy(
                     static source => source.HintName, StringComparer.Ordinal))
        {
            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(FileSeparator).Append(generated.HintName).Append('\n');
            builder.Append(generated.SourceText.ToString());
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Removes the differences that are not the generator's decisions: a BOM, the line-ending choice, and
    /// a trailing newline.
    /// </summary>
    /// <remarks>
    /// Line endings must be normalized because the emitter picks them —
    /// <c>RenderViewEmitter.IndentedWriter.AppendLine</c> calls <c>StringBuilder.AppendLine</c>, i.e.
    /// <c>Environment.NewLine</c> — so the same generator emits CRLF on Windows and LF elsewhere.  The
    /// trailing newline must be dropped because <c>RenderViewEmitter.Emit</c> ends with
    /// <c>Append("}")</c> and emits none, while <c>.editorconfig</c> sets <c>insert_final_newline</c>: an
    /// editor saving a baseline once would otherwise break every case, and the baselines are outside
    /// <c>dotnet format</c>'s document set so nothing would correct it.
    /// </remarks>
    private static string Normalize(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');

    private static string DescribeFirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var expectedLine = i < expectedLines.Length ? expectedLines[i] : null;
            var actualLine = i < actualLines.Length ? actualLines[i] : null;

            if (string.Equals(expectedLine, actualLine, StringComparison.Ordinal))
                continue;

            var builder = new StringBuilder();
            builder.Append("First difference at line ").Append(i + 1).Append(":\n");

            for (var context = Math.Max(0, i - 3); context < i; context++)
                builder.Append("      ").Append(expectedLines[context]).Append('\n');

            builder.Append("  -   ").Append(expectedLine ?? "<end of baseline>").Append('\n');
            builder.Append("  +   ").Append(actualLine ?? "<end of generated>").Append('\n');

            return builder.ToString();
        }

        // Reached only if the two differ in a way line-splitting cannot show, which Normalize rules out.
        return $"Baseline length {expected.Length}, generated length {actual.Length}.";
    }
}

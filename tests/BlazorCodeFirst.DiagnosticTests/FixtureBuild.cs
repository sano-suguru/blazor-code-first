using System.Collections.Immutable;

namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>The diagnostics one fixture project produced when real MSBuild built it.</summary>
public sealed class FixtureBuild
{
    internal FixtureBuild(
        string name,
        string projectDirectory,
        ImmutableArray<BuildDiagnostic> diagnostics,
        string buildOutput)
    {
        Name = name;
        ProjectDirectory = projectDirectory;
        Diagnostics = diagnostics;
        BuildOutput = buildOutput;
    }

    public string Name { get; }

    public string ProjectDirectory { get; }

    public ImmutableArray<BuildDiagnostic> Diagnostics { get; }

    /// <summary>Console output of the build, included in assertion failures so the cause is visible.</summary>
    public string BuildOutput { get; }

    public ImmutableArray<BuildDiagnostic> WithId(string id) =>
        [.. Diagnostics.Where(diagnostic => string.Equals(diagnostic.Id, id, StringComparison.Ordinal))];

    public bool Contains(string id) => WithId(id).Length > 0;

    /// <summary>
    /// Every reported diagnostic, one per line.  Assertions in this project attach this rather than a
    /// bare "expected X" because the interesting failure mode — a diagnostic vanishing for a reason
    /// unrelated to the one under test — is only readable from the full set.
    /// </summary>
    public string Report() =>
        $"Fixture '{Name}' reported {Diagnostics.Length} diagnostic(s):" +
        Environment.NewLine +
        (Diagnostics.IsEmpty
            ? "  (none)"
            : string.Join(Environment.NewLine, Diagnostics.Select(static d => "  " + d)));

    /// <summary>The source line a diagnostic points at, for asserting that a position is meaningful.</summary>
    public string SourceLineOf(BuildDiagnostic diagnostic)
    {
        Assert.True(diagnostic.HasLocation, $"{diagnostic.Id} was reported with no location.{Environment.NewLine}{Report()}");

        var lines = File.ReadAllLines(diagnostic.FilePath);
        Assert.InRange(diagnostic.Line, 1, lines.Length);
        return lines[diagnostic.Line - 1];
    }

    /// <summary>
    /// The source text the diagnostic's span covers — what the author sees squiggled.  Asserting on
    /// this rather than on raw line and column numbers keeps the pin precise without breaking every
    /// time a fixture file is reformatted.
    /// </summary>
    public string AnchorTextOf(BuildDiagnostic diagnostic)
    {
        Assert.True(
            diagnostic.HasLocation,
            $"{diagnostic.Id} was reported with no location.{Environment.NewLine}{Report()}");

        var lines = File.ReadAllLines(diagnostic.FilePath);
        Assert.InRange(diagnostic.Line, 1, lines.Length);
        Assert.InRange(diagnostic.EndLine, diagnostic.Line, lines.Length);

        if (diagnostic.EndLine == diagnostic.Line)
        {
            var line = lines[diagnostic.Line - 1];
            Assert.InRange(diagnostic.EndColumn - 1, diagnostic.Column - 1, line.Length);
            return line[(diagnostic.Column - 1)..(diagnostic.EndColumn - 1)];
        }

        var first = lines[diagnostic.Line - 1][(diagnostic.Column - 1)..];
        var middle = lines[diagnostic.Line..(diagnostic.EndLine - 1)];
        var last = lines[diagnostic.EndLine - 1][..(diagnostic.EndColumn - 1)];
        return string.Join(Environment.NewLine, [first, .. middle, last]);
    }
}

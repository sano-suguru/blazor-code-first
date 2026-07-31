using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.DiagnosticTests;

/// <summary>
/// Keeps <c>ARCHITECTURE.md</c> 付録A — the canonical diagnostic table, and the document a reader
/// actually consults — in step with the declared descriptor set in both directions.
/// <c>AnalyzerReleases.Unshipped.md</c> is already enforced by RS2000 at build time; the table is the
/// other half, and nothing checked it before (#86).
/// </summary>
public sealed class DiagnosticTableTests
{
    [Fact]
    public void AppendixA_HasARowForEveryDeclaredDescriptor()
    {
        var documented = AppendixA.DocumentedIds.ToImmutableHashSet(StringComparer.Ordinal);

        var missing = DeclaredDescriptors.Ids
            .Where(id => !documented.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(
            missing.IsEmpty,
            $"付録A does not document these declared diagnostics: {string.Join(", ", missing)}. " +
            $"Add a row to the 診断一覧 table in {AppendixA.DocumentPath}.");
    }

    [Fact]
    public void AppendixA_DocumentsOnlyDeclaredOrExplicitlyPendingDiagnostics()
    {
        var pending = DiagnosticExpectations.DocumentedWithoutDescriptor
            .Select(static entry => entry.Id)
            .ToImmutableHashSet(StringComparer.Ordinal);

        var undeclared = AppendixA.DocumentedIds
            .Where(id => !DeclaredDescriptors.Ids.Contains(id) && !pending.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(
            undeclared.IsEmpty,
            $"付録A documents diagnostics no descriptor declares: {string.Join(", ", undeclared)}. " +
            "Remove the row, or record the ID in DiagnosticExpectations.DocumentedWithoutDescriptor with " +
            "the reason it is specified ahead of its implementation.");
    }

    [Fact]
    public void DocumentedWithoutDescriptor_HoldsNoStaleEntry()
    {
        var documented = AppendixA.DocumentedIds.ToImmutableHashSet(StringComparer.Ordinal);

        var stale = DiagnosticExpectations.DocumentedWithoutDescriptor
            .Where(entry => DeclaredDescriptors.Ids.Contains(entry.Id) || !documented.Contains(entry.Id))
            .Select(static entry => entry.Id)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(
            stale.IsEmpty,
            $"These are excused from needing a descriptor but no longer need the excuse: {string.Join(", ", stale)}. " +
            "The descriptor landed, or the 付録A row is gone — either way, drop the entry so the exception " +
            "does not outlive its reason.");
    }

    [Fact]
    public void AppendixA_ListsEachDiagnosticOnce()
    {
        var repeated = AppendixA.DocumentedIds
            .GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"{group.Key} ({group.Count()} rows)")
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(repeated.IsEmpty, $"付録A documents the same diagnostic more than once: {string.Join(", ", repeated)}.");
    }

    [Fact]
    public void AppendixA_StatesASeverityForEveryRow()
    {
        var unreadable = AppendixA.Rows
            .Where(static row => !IsKnownKind(row.Kind))
            .Select(static row => $"{row.Id} (種別 reads {Quote(row.Kind)})")
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        // A row whose 種別 cell holds something other than a severity has nothing to compare, and a
        // guard that reports nothing to compare as agreement is worse than no guard.
        Assert.True(
            unreadable.IsEmpty,
            $"付録A does not state a severity for these diagnostics: {string.Join(", ", unreadable)}. " +
            $"It belongs in the second column and reads one of {string.Join(" / ", KnownKinds)}; if the columns " +
            "were reordered, reorder them back or teach AppendixA the new layout.");
    }

    [Fact]
    public void AppendixA_ClaimsTheSeverityEachDescriptorActuallyHas()
    {
        var pending = DiagnosticExpectations.DocumentedWithoutDescriptor
            .Select(static entry => entry.Id)
            .ToImmutableHashSet(StringComparer.Ordinal);

        var wrong = AppendixA.Rows
            .Where(row => IsKnownKind(row.Kind)
                && !pending.Contains(row.Id) && DeclaredDescriptors.Ids.Contains(row.Id))
            .Select(static row => (Row: row, Descriptor: DeclaredDescriptors.ById[row.Id]))
            .Select(static pair => (pair.Row.Id, Documented: pair.Row.Kind!, Actual: DocumentedKindOf(pair.Descriptor)))
            .Where(static pair => !string.Equals(pair.Documented, pair.Actual, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => $"{pair.Id} (付録A says {pair.Documented}, the descriptor is {pair.Actual})")
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(
            wrong.IsEmpty,
            $"付録A claims a severity the descriptor does not have: {string.Join(", ", wrong)}. " +
            "Changing a diagnostic's severity is a change to the canonical table too.");
    }

    [Fact]
    public void AppendixA_IsParsedAsATable()
    {
        // Every other assertion here reads as a pass if the parser silently matched nothing, so prove
        // it found rows before trusting what it did not find.
        Assert.NotEmpty(AppendixA.Rows);
    }

    /// <summary>Every severity 付録A has a wording for, and the only cell values worth comparing.</summary>
    private static ImmutableArray<string> KnownKinds { get; } = ["Error", "Warning", "Info"];

    /// <summary>
    /// Whether a 種別 cell states a severity at all.  Case is not the subject — the table is uniformly
    /// title-cased, but a lowercase cell is a typography slip, not a wrong claim — so this and the
    /// severity comparison ignore it, and both go through here so they cannot disagree about that.
    /// </summary>
    private static bool IsKnownKind(string? kind) =>
        kind is not null && KnownKinds.Contains(kind, StringComparer.OrdinalIgnoreCase);

    /// <summary>Renders an offending cell short enough to read in a failure message.</summary>
    private static string Quote(string? kind) =>
        kind is null ? "empty" : $"'{(kind.Length > 40 ? kind[..40] + "…" : kind)}'";

    /// <summary>
    /// The severity as 付録A words it.  Deliberately not shared with
    /// <c>DescriptorCoverageTests.ExpectedSeverity_MatchesTheDescriptor</c>: that one speaks SARIF, where
    /// <see cref="DiagnosticSeverity.Info"/> is reported as <c>note</c>, while the table writes
    /// <c>Info</c>.  Both derive from <see cref="DiagnosticSeverity"/> rather than from each other.
    /// </summary>
    private static string DocumentedKindOf(DiagnosticDescriptor descriptor) =>
        descriptor.DefaultSeverity switch
        {
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            DiagnosticSeverity.Info => "Info",
            // 付録A has never had to word one of these, so there is no convention to check against yet.
            // Whoever adds the first such descriptor decides what the table says, here and there.
            _ => throw new InvalidOperationException(
                $"{descriptor.Id} has severity {descriptor.DefaultSeverity}, which 付録A has no wording for."),
        };
}

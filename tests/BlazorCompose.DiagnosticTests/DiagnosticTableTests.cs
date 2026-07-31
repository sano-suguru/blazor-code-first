using System.Collections.Immutable;

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
    public void AppendixA_IsParsedAsATable()
    {
        // Every other assertion here reads as a pass if the parser silently matched nothing, so prove
        // it found rows before trusting what it did not find.
        Assert.NotEmpty(AppendixA.DocumentedIds);
    }
}

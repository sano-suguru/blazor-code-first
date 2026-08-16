using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>
/// Keeps the documentation site's diagnostic reference in step with the declared descriptor set in
/// both directions, the way <see cref="DiagnosticTableTests"/> keeps <c>ARCHITECTURE.md</c> 付録A.
/// </summary>
/// <remarks>
/// <para>
/// Two documents, two guards, and neither derives from the other. 付録A is Japanese and written for
/// whoever implements a diagnostic: which component reports it, and why the implementation sits where
/// it does. The page is English and written for whoever hit one. What they share is the descriptor
/// set, so each is held to that rather than to its counterpart.
/// </para>
/// <para>
/// Here rather than under <c>site/</c> because the subject of every assertion below is
/// <c>DiagnosticDescriptors</c>, and nothing there can reference the compiler. The page is read from
/// the working tree by path, the arrangement <see cref="AppendixA"/> already uses.
/// </para>
/// </remarks>
public sealed class DiagnosticsPageTests
{
    [Fact]
    public void DiagnosticsPage_HasAnEntryForEveryDeclaredDescriptor()
    {
        var documented = DiagnosticsPage.DocumentedIds.ToImmutableHashSet(StringComparer.Ordinal);

        var missing = DeclaredDescriptors.Ids
            .Where(id => !documented.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(
            missing.IsEmpty,
            $"The site's diagnostic reference does not document these declared diagnostics: " +
            $"{string.Join(", ", missing)}. Add an entry to {DiagnosticsPage.DocumentPath}.");
    }

    [Fact]
    public void DiagnosticsPage_DocumentsOnlyDeclaredDiagnostics()
    {
        var undeclared = DiagnosticsPage.DocumentedIds
            .Where(id => !DeclaredDescriptors.Ids.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        // Stricter than DiagnosticTableTests, which excuses an ID recorded in
        // DocumentedWithoutDescriptor. Specifying a diagnostic ahead of its implementation is 付録A's
        // business; a reader arrives here from a build failure, so an entry for a diagnostic nothing
        // can report is an entry nobody can arrive at.
        Assert.True(
            undeclared.IsEmpty,
            $"The site's diagnostic reference documents diagnostics no descriptor declares: " +
            $"{string.Join(", ", undeclared)}. Remove the entry, or land the descriptor. Specifying one " +
            "ahead of its implementation belongs in 付録A, which has a register for it.");
    }

    [Fact]
    public void DiagnosticsPage_ListsEachDiagnosticOnce()
    {
        var repeated = DiagnosticsPage.DocumentedIds
            .GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"{group.Key} ({group.Count()} entries)")
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(
            repeated.IsEmpty,
            $"The site's diagnostic reference documents the same diagnostic more than once: " +
            $"{string.Join(", ", repeated)}. Each heading is the anchor a document links to, and a " +
            "repeated one publishes a second anchor nothing links to.");
    }

    [Fact]
    public void DiagnosticsPage_ClaimsTheSeverityEachDescriptorActuallyHas()
    {
        var wrong = DiagnosticsPage.Entries
            .Where(entry => DeclaredDescriptors.Ids.Contains(entry.Id))
            .Select(static entry => (
                entry.Id,
                Documented: entry.Kind,
                Actual: SeverityOf(DeclaredDescriptors.ById[entry.Id])))
            .Where(static pair => !string.Equals(pair.Documented, pair.Actual, StringComparison.Ordinal))
            .Select(static pair =>
                $"{pair.Id} (the page says {pair.Documented ?? "nothing"}, the descriptor is {pair.Actual})")
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(
            wrong.IsEmpty,
            $"The site's diagnostic reference claims a severity the descriptor does not have: " +
            $"{string.Join(", ", wrong)}. Changing a diagnostic's severity is a change to this page too. " +
            "An entry claiming nothing opens its first paragraph with something other than the severity.");
    }

    [Fact]
    public void DiagnosticsPage_IsParsedAsEntries() =>
        // Every assertion above reads as a pass if the parser silently matched nothing, so prove it
        // found entries before trusting what it did not find.
        Assert.NotEmpty(DiagnosticsPage.Entries);

    /// <summary>The severity as the page words it.</summary>
    /// <remarks>
    /// Derived from <see cref="DiagnosticSeverity"/> rather than from 付録A's wording, for the reason
    /// <c>DiagnosticTableTests.DocumentedKindOf</c> gives: two documents that read each other drift
    /// together. The words match today, and neither is bound to the other.
    /// </remarks>
    private static string SeverityOf(DiagnosticDescriptor descriptor) =>
        descriptor.DefaultSeverity switch
        {
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            DiagnosticSeverity.Info => "Info",
            // The page has never had to word one of these, so there is no convention to check against
            // yet. Whoever adds the first such descriptor decides what it says, here and in 付録A.
            _ => throw new InvalidOperationException(
                $"{descriptor.Id} has severity {descriptor.DefaultSeverity}, which the page has no wording for."),
        };
}

using System.Collections.Immutable;
using System.Reflection;
using BlazorCompose.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.DiagnosticTests;

/// <summary>
/// Keeps <see cref="DiagnosticExpectations"/> honest against the real descriptor set, so a diagnostic
/// added to the compiler cannot quietly skip the only layer that proves it reaches a build.
/// <c>KnownSymbolsSyncTests</c> is the precedent for this shape of guard.
/// </summary>
public sealed class DescriptorCoverageTests
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Declared =
        [.. typeof(DiagnosticDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(static field => (DiagnosticDescriptor)field.GetValue(null)!)];

    [Fact]
    public void EveryDeclaredDescriptor_IsExercisedOrExplicitlyExcluded()
    {
        var covered = DiagnosticExpectations.All
            .Select(static expectation => expectation.Id)
            .Concat(DiagnosticExpectations.Excluded.Select(static excluded => excluded.Id))
            .ToImmutableHashSet(StringComparer.Ordinal);

        var missing = Declared
            .Select(static descriptor => descriptor.Id)
            .Where(id => !covered.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();

        Assert.True(
            missing.IsEmpty,
            $"No real build proves these diagnostics are reachable: {string.Join(", ", missing)}. " +
            "Add a fixture shape and an entry to DiagnosticExpectations.All, or an argued reason to " +
            "DiagnosticExpectations.Excluded.");
    }

    [Fact]
    public void EveryExpectation_NamesADeclaredDescriptor()
    {
        var declaredIds = Declared.Select(static descriptor => descriptor.Id).ToImmutableHashSet(StringComparer.Ordinal);

        var unknown = DiagnosticExpectations.All
            .Select(static expectation => expectation.Id)
            .Concat(DiagnosticExpectations.Excluded.Select(static excluded => excluded.Id))
            .Where(id => !declaredIds.Contains(id))
            .ToImmutableArray();

        Assert.True(unknown.IsEmpty, $"Expectations name diagnostics that no longer exist: {string.Join(", ", unknown)}.");
    }

    [Fact]
    public void NoDiagnostic_IsBothExercisedAndExcluded()
    {
        var both = DiagnosticExpectations.All
            .Select(static expectation => expectation.Id)
            .Where(static id => DiagnosticExpectations.Excluded.Any(excluded => string.Equals(excluded.Id, id, StringComparison.Ordinal)))
            .ToImmutableArray();

        Assert.True(both.IsEmpty, $"Exercised and excluded at the same time: {string.Join(", ", both)}.");
    }

    [Theory]
    [MemberData(nameof(DiagnosticExpectations.Ids), MemberType = typeof(DiagnosticExpectations))]
    public void ExpectedSeverity_MatchesTheDescriptor(string id)
    {
        // The severity in the table is asserted against a real build, so it must be the descriptor's
        // own default rather than whatever the fixture happened to produce.
        var expected = DiagnosticExpectations.For(id);
        var descriptor = Declared.Single(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));

        var severity = descriptor.DefaultSeverity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Info => "note",
            _ => throw new InvalidOperationException(
                $"{descriptor.Id} has severity {descriptor.DefaultSeverity}, which SARIF does not report."),
        };

        Assert.Equal(severity, expected.Severity);
    }
}
